let ctx = null;
let canvas = null;
let width = 0;
let height = 0;
const MAX_DISTANCE = 4000; // 最大距离 4000mm

// 存储点数据
let pointBuffer = [];
const MAX_POINTS = 3000;

// 颜色配置
const COLOR_RADAR = '#39ff14';
const COLOR_GRID = 'rgba(20, 80, 20, 0.4)';
const COLOR_ZONE_SAFE = 'rgba(255, 255, 0, 0.5)'; // 黄色虚线 (安全)
const COLOR_ZONE_ALERT = 'rgba(255, 51, 51, 0.4)'; // 红色高亮 (报警)

// --- 防御区状态变量 (修改为数组以支持多区域) ---
let dotNetRef = null; // C# 引用
let zones = [];       // 数组：存储所有防御区对象 { x, y, radius, isTriggered, id }

// 全局报警状态（用于避免重复发送日志）
let isGlobalAlarmTriggered = false;

// 交互状态
let interactionMode = 'none'; // 'none', 'drawing'
let drawStart = null;         // 鼠标按下时的坐标
let tempZone = null;          // 正在拖拽中的临时区域

export function initLidar(canvasId, ref) {
    canvas = document.getElementById(canvasId);
    if (!canvas) return;

    dotNetRef = ref; // 保存 C# 引用以便回调
    ctx = canvas.getContext('2d');

    // 启用混合模式以增强光效
    ctx.globalCompositeOperation = 'lighter';

    // 绑定鼠标事件
    canvas.addEventListener('mousedown', handleMouseDown);
    canvas.addEventListener('mousemove', handleMouseMove);
    canvas.addEventListener('mouseup', handleMouseUp);

    resizeCanvas();
    window.addEventListener('resize', resizeCanvas);
    requestAnimationFrame(drawLoop);
}

// 供 C# 调用：开始绘制模式
export function startZoneDrawing() {
    interactionMode = 'drawing';
    canvas.style.cursor = 'crosshair';
}

// 供 C# 调用：清除所有区域
export function clearZone() {
    zones = []; // 清空数组
    isGlobalAlarmTriggered = false;
    interactionMode = 'none';
    canvas.style.cursor = 'default';
}

// --- 鼠标交互逻辑 ---
function handleMouseDown(e) {
    if (interactionMode !== 'drawing') return;
    const rect = canvas.getBoundingClientRect();
    drawStart = {
        x: e.clientX - rect.left,
        y: e.clientY - rect.top
    };
}

function handleMouseMove(e) {
    if (interactionMode !== 'drawing' || !drawStart) return;

    const rect = canvas.getBoundingClientRect();
    const currentX = e.clientX - rect.left;
    const currentY = e.clientY - rect.top;

    // 实时计算半径
    const dx = currentX - drawStart.x;
    const dy = currentY - drawStart.y;
    const radius = Math.sqrt(dx * dx + dy * dy);

    // 存储临时区域用于显示
    tempZone = {
        cx: drawStart.x,
        cy: drawStart.y,
        r: radius
    };
}

function handleMouseUp(e) {
    if (interactionMode !== 'drawing' || !drawStart) return;

    const rect = canvas.getBoundingClientRect();
    const currentX = e.clientX - rect.left;
    const currentY = e.clientY - rect.top;

    const dx = currentX - drawStart.x;
    const dy = currentY - drawStart.y;
    const radius = Math.sqrt(dx * dx + dy * dy);

    if (radius > 5) {
        // 保存区域 (转换为相对于画布中心的偏移)
        // 使用 push 添加到数组，实现多区域
        zones.push({
            x: drawStart.x - (width / 2),
            y: drawStart.y - (height / 2),
            radius: radius,
            isTriggered: false,
            id: Date.now() // 唯一ID
        });
    }

    // 结束绘制
    drawStart = null;
    tempZone = null;
    interactionMode = 'none';
    canvas.style.cursor = 'default';

    // 通知 C# 绘制完成
    if (dotNetRef) dotNetRef.invokeMethodAsync("OnZoneSet");
}

function resizeCanvas() {
    if (!canvas) return;
    const parent = canvas.parentElement;
    canvas.width = parent.clientWidth;
    canvas.height = parent.clientHeight;
    width = canvas.width;
    height = canvas.height;
}

export function pushData(newPoints) {
    pointBuffer.push(...newPoints);
    if (pointBuffer.length > MAX_POINTS) {
        pointBuffer = pointBuffer.slice(pointBuffer.length - MAX_POINTS);
    }
}

function drawLoop() {
    if (!ctx) return;

    // --- 拖影效果 ---
    ctx.globalCompositeOperation = 'source-over';
    ctx.fillStyle = 'rgba(2, 11, 2, 0.15)';
    ctx.fillRect(0, 0, width, height);

    ctx.globalCompositeOperation = 'lighter';

    const centerX = width / 2;
    const centerY = height / 2;
    const scale = (Math.min(width, height) / 2) * 0.9 / MAX_DISTANCE;

    // --- 绘制静态刻度圈 ---
    drawRings(centerX, centerY, scale);

    // 重置本帧碰撞状态
    let frameHitDetected = false;
    // 重置所有区域的当帧触发状态(用于视觉)，保留 persistent 状态在逻辑中处理
    zones.forEach(z => z.currentFrameHit = false);

    // --- 绘制所有激光点 ---
    ctx.fillStyle = COLOR_RADAR;

    for (let i = 0; i < pointBuffer.length; i++) {
        const p = pointBuffer[i];

        // 过滤
        if (p.distance > MAX_DISTANCE || p.distance < 10) continue;

        const r = p.distance * scale;
        const angle = p.angle - (Math.PI / 2);

        // 算出点的屏幕坐标
        const x = centerX + r * Math.cos(angle);
        const y = centerY + r * Math.sin(angle);

        ctx.fillRect(x, y, 2.5, 2.5);

        // --- 核心功能：多区域碰撞检测 ---
        if (zones.length > 0) {
            let pointHit = false;
            for (let z of zones) {
                const zoneScreenX = centerX + z.x;
                const zoneScreenY = centerY + z.y;
                const distToZone = Math.hypot(x - zoneScreenX, y - zoneScreenY);

                if (distToZone < z.radius) {
                    pointHit = true;
                    z.currentFrameHit = true; // 标记该区域被击中
                    frameHitDetected = true;
                }
            }

            if (pointHit) {
                // 碰撞的点画成红色
                ctx.fillStyle = '#ff3333';
                ctx.fillRect(x - 1, y - 1, 4, 4);
                ctx.fillStyle = COLOR_RADAR; // 还原颜色给下一个点
            }
        }
    }

    // --- 处理报警状态与回调 ---
    if (zones.length > 0) {
        if (frameHitDetected) {
            if (!isGlobalAlarmTriggered) {
                isGlobalAlarmTriggered = true;
                if (dotNetRef) dotNetRef.invokeMethodAsync("TriggerAlarm", "检测到入侵目标!");
            }
        } else {
            // 没人了，自动复位全局报警状态
            isGlobalAlarmTriggered = false;
        }

        // --- 绘制所有防御区 ---
        for (let z of zones) {
            const zoneScreenX = centerX + z.x;
            const zoneScreenY = centerY + z.y;
            // 如果该区域当前帧有碰撞，则显示红色
            drawZone(zoneScreenX, zoneScreenY, z.radius, z.currentFrameHit);
        }
    }

    // 绘制正在拖拽中的临时圆
    if (interactionMode === 'drawing' && tempZone) {
        drawZone(tempZone.cx, tempZone.cy, tempZone.r, false, true);
    }

    // --- 绘制扫描线 ---
    drawScanLine(centerX, centerY);

    // --- 十字准星 ---
    drawCrosshair(centerX, centerY);

    requestAnimationFrame(drawLoop);
}

function drawZone(x, y, r, isAlert, isDashed = false) {
    ctx.beginPath();
    ctx.arc(x, y, r, 0, Math.PI * 2);

    if (isAlert) {
        ctx.fillStyle = COLOR_ZONE_ALERT; // 报警时：区域亮起且透明
        ctx.fill();
        ctx.strokeStyle = '#ff3333';
        ctx.lineWidth = 3; // 加粗线条
        ctx.shadowBlur = 15;
        ctx.shadowColor = '#ff0000';
    } else {
        ctx.strokeStyle = '#ffff00';
        ctx.lineWidth = 1;
        ctx.fillStyle = 'rgba(255, 255, 0, 0.05)'; // 平时极淡的填充
        ctx.fill();
        ctx.shadowBlur = 0;
    }

    if (isDashed || (!isAlert && !isDashed)) ctx.setLineDash([5, 5]);
    if (isAlert) ctx.setLineDash([]); // 报警时实线

    ctx.stroke();
    ctx.setLineDash([]);
    ctx.shadowBlur = 0; // 重置光晕，避免影响文字

    // 标签 - 修改了字体大小
    if (!isDashed) {
        if (isAlert) {
            // 报警状态：大字体
            ctx.font = 'bold 24px Consolas'; // 修改：字体变大
            const text = "WARNING";
            const textWidth = ctx.measureText(text).width;

            // 文字背景框，增强可读性
            ctx.fillStyle = 'rgba(0,0,0,0.7)';
            ctx.fillRect(x - textWidth / 2 - 4, y - r - 28, textWidth + 8, 28);

            ctx.fillStyle = '#ff3333';
            ctx.textAlign = 'center';
            ctx.fillText(text, x, y - r - 6);
            ctx.textAlign = 'start'; // 还原对齐
        } else {
            // 正常状态
            ctx.font = '12px Consolas';
            ctx.fillStyle = '#ffff00';
            ctx.fillText("ZONE ACTIVE", x - 20, y - r - 5);
        }
    }
}

function drawScanLine(centerX, centerY) {
    const time = Date.now() / 1000;
    const scanSpeed = 2.0;
    const scanAngle = (time * scanSpeed * Math.PI) % (Math.PI * 2);

    const gradient = ctx.createConicGradient(scanAngle + Math.PI / 2, centerX, centerY);
    gradient.addColorStop(0, 'rgba(57, 255, 20, 0)');
    gradient.addColorStop(1, 'rgba(57, 255, 20, 0.06)');

    ctx.fillStyle = gradient;
    ctx.beginPath();
    ctx.arc(centerX, centerY, Math.min(width, height), 0, Math.PI * 2);
    ctx.fill();
}

function drawRings(cx, cy, scale) {
    ctx.strokeStyle = COLOR_GRID;
    ctx.lineWidth = 1;
    ctx.setLineDash([5, 5]);

    const steps = 4;
    for (let i = 1; i <= steps; i++) {
        ctx.beginPath();
        const dist = (MAX_DISTANCE / steps) * i;
        const r = dist * scale;
        ctx.arc(cx, cy, r, 0, 2 * Math.PI);
        ctx.stroke();

        ctx.fillStyle = 'rgba(57, 255, 20, 0.7)';
        ctx.font = '10px Consolas';
        ctx.fillText(`${dist}mm`, cx + 2, cy - r - 2);
    }
    ctx.setLineDash([]);
}

function drawCrosshair(cx, cy) {
    ctx.strokeStyle = 'rgba(57, 255, 20, 0.3)';
    ctx.lineWidth = 1;
    const size = 20;

    ctx.beginPath();
    ctx.moveTo(cx - size, cy);
    ctx.lineTo(cx + size, cy);
    ctx.moveTo(cx, cy - size);
    ctx.lineTo(cx, cy + size);
    ctx.stroke();
}