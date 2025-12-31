using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;

namespace BlazorLidar.Data
{
    // 定义数据点结构
    public readonly struct LidarPoint
    {
        public double Angle { get; }
        public double Distance { get; }
        public LidarPoint(double angle, double distance)
        {
            Angle = angle;
            Distance = distance;
        }
    }

    public class LidarService : IDisposable
    {
        private SerialPort? _serialPort;
        private Thread? _readThread;
        private volatile bool _isRunning; 
        private readonly ConcurrentQueue<LidarPoint> _dataQueue = new();

        public event Action? OnDataReceived;

        public void Start(string portName, int baudRate)
        {
            if (_isRunning) return;

            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                    _serialPort.Close();

                _serialPort = new SerialPort(portName, baudRate);
                _serialPort.ReadBufferSize = 8192;
                _serialPort.ReadTimeout = 1000;
                _serialPort.Open();

                _isRunning = true;
                _readThread = new Thread(ReadSerialLoop)
                {
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal
                };
                _readThread.Start();
                Console.WriteLine($"[LidarService] Started on {portName} @ {baudRate}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LidarService] Error: {ex.Message}");
                _isRunning = false;
                throw;
            }
        }

        public void Stop()
        {
            _isRunning = false;
            Thread.Sleep(50);

            if (_serialPort != null && _serialPort.IsOpen)
            {
                try { _serialPort.Close(); } catch { }
            }
            Console.WriteLine("[LidarService] Stopped.");
        }

        public List<LidarPoint> FetchNewPoints()
        {
            var points = new List<LidarPoint>();
            int limit = 2000;
            while (_dataQueue.TryDequeue(out var point) && limit-- > 0)
            {
                points.Add(point);
            }
            return points;
        }

        private void ReadSerialLoop()
        {
            int syncState = 0;
            byte[] headerBuffer = new byte[2];
            byte[] payloadBuffer = new byte[1024];

            while (_isRunning && _serialPort != null && _serialPort.IsOpen)
            {
                try
                {
                    if (syncState == 0)
                    {
                        if (_serialPort.ReadByte() == 0xAA) syncState = 1;
                    }
                    else if (syncState == 1)
                    {
                        if (_serialPort.ReadByte() == 0x55)
                        {
                            ReadBytes(_serialPort, headerBuffer, 2);
                            int lsn = headerBuffer[1];
                            if (lsn == 0) { syncState = 0; continue; }

                            int remainingBytes = 4 + (lsn * 3);
                            ReadBytes(_serialPort, payloadBuffer, remainingBytes);
                            ParseAndEnqueue(headerBuffer, payloadBuffer, lsn);
                            
                            syncState = 0;
                        }
                        else
                        {
                            syncState = 0;
                        }
                    }
                }
                catch (TimeoutException) { }
                catch (Exception ex)
                {
                    if (_isRunning) Console.WriteLine($"Read Loop Warn: {ex.Message}");
                    Thread.Sleep(100);
                    syncState = 0;
                }
            }
        }

        private static void ReadBytes(SerialPort port, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = port.Read(buffer, offset, count - offset);
                if (read == 0) throw new Exception("End of stream");
                offset += read;
            }
        }

        private void ParseAndEnqueue(byte[] header, byte[] payload, int lsn)
        {
            ushort fsAngleRaw = (ushort)(payload[0] | (payload[1] << 8));
            ushort lsAngleRaw = (ushort)(payload[2] | (payload[3] << 8));

            double angleStart = (fsAngleRaw >> 1) / 64.0;
            double angleEnd = (lsAngleRaw >> 1) / 64.0;

            double diffAngle = (lsn > 1) ? (angleEnd - angleStart) : 0.0;
            if (diffAngle < 0) diffAngle += 360;

            for (int i = 0; i < lsn; i++)
            {
                int offset = 4 + (i * 3);
                ushort distRaw = (ushort)(payload[offset] | (payload[offset + 1] << 8));
                double distance = distRaw / 4.0;

                if (distance > 0)
                {
                    double angle;
                    if (lsn > 1)
                        angle = (diffAngle / (lsn - 1)) * i + angleStart;
                    else
                        angle = angleStart;

                    double angleRad = (angle % 360) * Math.PI / 180.0;
                    _dataQueue.Enqueue(new LidarPoint(angleRad, distance));
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}