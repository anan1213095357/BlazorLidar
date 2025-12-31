using System;
using System.Collections.Generic;
using System.Drawing; // 如果是在 Server 端处理图片，或者只用纯数据结构
using System.Numerics;

namespace BlazorLidar.Data
{
    public class GridMapper
    {
        // 地图尺寸定义
        public int Width { get; }
        public int Height { get; }
        public double Resolution { get; } // 每个像素代表多少米 (例如 0.05米/像素)
        
        // 地图中心在数组中的偏移量
        private readonly int _centerX;
        private readonly int _centerY;

        // 栅格数据：0 = 未知, 1-100 = 概率占据, 255 = 绝对占据
        // 简化版：我们用 sbyte。0=空闲, 100=墙, -1=未知
        private readonly sbyte[] _gridData; 

        public GridMapper(int width, int height, double resolution)
        {
            Width = width;
            Height = height;
            Resolution = resolution;
            _gridData = new sbyte[width * height];
            
            // 初始化为 -1 (未知区域)
            Array.Fill(_gridData, (sbyte)-1);

            // 假设雷达在地图中心
            _centerX = width / 2;
            _centerY = height / 2;
        }

        // 获取地图原始数据供 UI 渲染
        public sbyte[] GetMapData() => _gridData;

        // 核心方法：处理一帧雷达数据
        public void UpdateMap(List<LidarPoint> points)
        {
            // 每次更新也可以选择缓慢衰减旧数据（可选）
            
            foreach (var point in points)
            {
                if (point.Distance <= 0) continue;

                // 1. 极坐标 -> 笛卡尔坐标 (单位：米)
                // 注意：LidarPoint.Distance 单位如果是 mm，需要除以 1000
                double distMeters = point.Distance / 1000.0;
                
                // 计算击中点的物理坐标
                double hitX = distMeters * Math.Cos(point.Angle);
                double hitY = distMeters * Math.Sin(point.Angle);

                // 2. 物理坐标 -> 栅格坐标
                int gridX = (int)(hitX / Resolution) + _centerX;
                int gridY = (int)(hitY / Resolution) + _centerY;

                // 3. Bresenham 算法：雷达位置到击中点之间的区域是“空闲”的
                TraceLine(_centerX, _centerY, gridX, gridY);

                // 4. 更新击中点为“占据”
                SetCell(gridX, gridY, 100); 
            }
        }

        private void SetCell(int x, int y, sbyte value)
        {
            if (x >= 0 && x < Width && y >= 0 && y < Height)
            {
                int index = y * Width + x;
                
                // 简单的概率更新策略：
                // 如果本来是未知(-1)，直接赋值
                // 如果是空闲(0) 要变 墙(100)，直接赋值
                // 实际项目中通常使用 Log-Odds 算法进行累加
                
                // 这里使用最简单的“覆盖”逻辑演示：
                _gridData[index] = value;
            }
        }

        // 布雷森汉姆直线算法：用于找出两点之间的所有格子（将它们设为空闲）
        private void TraceLine(int x0, int y0, int x1, int y1)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy, e2;

            while (true)
            {
                // 不要清除终点（那是墙），只清除路径
                if (x0 == x1 && y0 == y1) break; 

                SetCell(x0, y0, 0); // 设置为 0 (Free/空闲)

                e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }
    }
}