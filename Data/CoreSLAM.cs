using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BlazorLidar.Data
{
    public class Pose
    {
        public double X { get; set; } // 单位：米
        public double Y { get; set; } // 单位：米
        public double Theta { get; set; } // 单位：弧度

        public Pose(double x, double y, double theta) { X = x; Y = y; Theta = theta; }
    }

    public class CoreSLAM
    {
        // 地图配置
        public int Width { get; }
        public int Height { get; }
        public double Resolution { get; } // 米/像素
        public byte[] MapData { get; private set; } // 0=空闲, 127=未知, 255=墙 (方便前端绘图)

        // 内部高精度地图 (用于计算)
        private ushort[] _internalMap;
        private const ushort MAP_UNKNOWN = 0x8000;
        private const ushort MAP_WALL = 0xFFFF;
        private const ushort MAP_EMPTY = 0x0000;

        // 机器人位姿
        public Pose CurrentPose { get; private set; }

        // 扫描匹配参数
        private const int SEARCH_WINDOW = 20; // 搜索范围(像素)
        private const double ANGLE_WINDOW = 0.2; // 角度搜索范围(弧度)
        private const int ITERATIONS = 3000; // 蒙特卡洛随机采样次数

        public CoreSLAM(int width, int height, double resolution)
        {
            Width = width;
            Height = height;
            Resolution = resolution;

            // 初始化位于地图中心
            CurrentPose = new Pose(width * resolution / 2.0, height * resolution / 2.0, 0);

            MapData = new byte[width * height];
            _internalMap = new ushort[width * height];

            // 初始化地图为未知
            Array.Fill(MapData, (byte)127);
            Array.Fill(_internalMap, MAP_UNKNOWN);
        }

        // 核心方法：传入雷达点，返回更新后的位姿
        public Pose ProcessScan(List<LidarPoint> points)
        {
            if (points.Count == 0) return CurrentPose;

            // 1. 扫描匹配 (Scan Matching) - 寻找最佳位姿
            // 如果是第一帧，跳过匹配直接建图
            bool isFirstFrame = (_internalMap[(int)(CurrentPose.Y / Resolution) * Width + (int)(CurrentPose.X / Resolution)] == MAP_UNKNOWN);

            if (!isFirstFrame)
            {
                CurrentPose = EstimatePose(points, CurrentPose);
            }

            // 2. 更新地图 (Mapping)
            UpdateMap(points, CurrentPose);

            return CurrentPose;
        }

        // 简单的蒙特卡洛搜索：寻找让雷达点和现有墙壁重合度最高的位置
        private Pose EstimatePose(List<LidarPoint> points, Pose startPose)
        {
            double bestScore = -1;
            Pose bestPose = new Pose(startPose.X, startPose.Y, startPose.Theta);

            // 随机撒点搜索 (简化版 TS 算法)
            var rand = new Random();

            for (int i = 0; i < ITERATIONS; i++)
            {
                // 在当前位置附近随机生成假设位置
                double dx = (rand.NextDouble() - 0.5) * Resolution * SEARCH_WINDOW; // +/- 搜索范围
                double dy = (rand.NextDouble() - 0.5) * Resolution * SEARCH_WINDOW;
                double dth = (rand.NextDouble() - 0.5) * ANGLE_WINDOW;

                // 如果是第一次迭代，测试原点（假设静止）
                if (i == 0) { dx = 0; dy = 0; dth = 0; }

                double testX = startPose.X + dx;
                double testY = startPose.Y + dy;
                double testTh = startPose.Theta + dth;

                double score = CalculateScore(points, testX, testY, testTh);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPose.X = testX;
                    bestPose.Y = testY;
                    bestPose.Theta = testTh;
                }
            }

            return bestPose;
        }

        private double CalculateScore(List<LidarPoint> points, double px, double py, double ptheta)
        {
            double score = 0;
            int step = 2; // 为了性能，每隔几个点采样一次

            for (int i = 0; i < points.Count; i += step)
            {
                var p = points[i];
                if (p.Distance <= 0.1 || p.Distance > 10) continue;

                double dist = p.Distance / 1000.0; // mm -> m
                double angle = p.Angle + ptheta;

                double hitX = px + dist * Math.Cos(angle);
                double hitY = py + dist * Math.Sin(angle);

                int gx = (int)(hitX / Resolution);
                int gy = (int)(hitY / Resolution);

                if (gx >= 0 && gx < Width && gy >= 0 && gy < Height)
                {
                    ushort val = _internalMap[gy * Width + gx];
                    // 如果落点正好是墙(65535)或大概率是墙，加分
                    if (val > 40000 && val != MAP_UNKNOWN) score += 1.0;
                    // 如果落点是空闲区域，减分（惩罚）
                    else if (val < 20000 && val != MAP_UNKNOWN) score -= 0.5;
                }
            }
            return score;
        }

        private void UpdateMap(List<LidarPoint> points, Pose pose)
        {
            int cx = (int)(pose.X / Resolution);
            int cy = (int)(pose.Y / Resolution);

            foreach (var p in points)
            {
                if (p.Distance <= 0.1) continue;
                double dist = p.Distance / 1000.0;
                double angle = p.Angle + pose.Theta;

                double hitX = pose.X + dist * Math.Cos(angle);
                double hitY = pose.Y + dist * Math.Sin(angle);

                int gx = (int)(hitX / Resolution);
                int gy = (int)(hitY / Resolution);

                // Bresenham 射线追踪：清除路径上的障碍物（设为空闲）
                TraceLine(cx, cy, gx, gy);

                // 更新击中点为墙
                UpdateCell(gx, gy, true);
            }
        }

        private void TraceLine(int x0, int y0, int x1, int y1)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy, e2;

            while (true)
            {
                if (x0 == x1 && y0 == y1) break;
                UpdateCell(x0, y0, false); // 设为空闲
                e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        private void UpdateCell(int x, int y, bool isHit)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return;
            int idx = y * Width + x;

            // 简单的概率更新模型
            // Unknown(32768) -> Hit -> +Value
            // Unknown(32768) -> Miss -> -Value

            int current = _internalMap[idx];
            if (current == MAP_UNKNOWN) current = 32768;

            if (isHit)
                current = Math.Min(65535, current + 2000); // 增加占据概率
            else
                current = Math.Max(0, current - 500); // 减少占据概率 (变为空闲)

            _internalMap[idx] = (ushort)current;

            // 同步到显示用的 MapData
            if (current > 45000) MapData[idx] = 255; // 墙
            else if (current < 20000) MapData[idx] = 0; // 空
            else MapData[idx] = 127; // 未知
        }
    }
}