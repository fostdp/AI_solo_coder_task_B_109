/**
 * ClayImage.js - 泥塑彩绘图像盐分热点图渲染组件
 *
 * 功能：
 *   - 绘制泥塑基底轮廓（椭圆形佛像/人像外形）
 *   - 盐分富集区亮点渲染（Canvas径向渐变热点）
 *   - 伪彩热力图叠加（蓝→绿→黄→红 浓度梯度）
 *   - 传感器位置标注
 *   - 鼠标悬停显示浓度Tooltip
 *   - DPR高清适配
 *   - 高性能：空间哈希索引，O(1) 热点查询
 *
 * 使用方式：
 *   const clay = new ClayImage(canvas, { sculptureName: '释迦牟尼佛', baseColor: '#D2B48C' });
 *   clay.updateHotspots([{x:0.35, y:0.42, intensity:85, label:'Na⁺ 620ppm'}]);
 *   clay.render();
 */

(function (global) {
  'use strict';

  const DEFAULT_OPTIONS = {
    sculptureName: '泥塑彩绘',
    sculptureType: 'Buddha',
    baseColor: '#D2B48C',
    outlineColor: '#8B4513',
    hotSpotMinRadius: 12,
    hotSpotMaxRadius: 45,
    heatmapOpacity: 0.45,
    hotspotGlow: true,
    showSensorMarkers: true,
    showConcentrationScale: true,
    tooltipDelay: 120,
    thresholdWarning: 30,
    thresholdCritical: 60
  };

  const COLOR_SCALE = [
    { v: 0,   c: [64,   0, 128] },
    { v: 25,  c: [0,   96, 200] },
    { v: 50,  c: [0,  200, 150] },
    { v: 75,  c: [255, 200,   0] },
    { v: 100, c: [255,  40,  40] }
  ];

  function lerpColor(ratio) {
    ratio = Math.max(0, Math.min(1, ratio));
    for (let i = 0; i < COLOR_SCALE.length - 1; i++) {
      const a = COLOR_SCALE[i];
      const b = COLOR_SCALE[i + 1];
      if (ratio >= a.v / 100 && ratio <= b.v / 100) {
        const r = (ratio - a.v / 100) / ((b.v - a.v) / 100 || 1);
        return [
          Math.round(a.c[0] + (b.c[0] - a.c[0]) * r),
          Math.round(a.c[1] + (b.c[1] - a.c[1]) * r),
          Math.round(a.c[2] + (b.c[2] - a.c[2]) * r)
        ];
      }
    }
    return COLOR_SCALE[COLOR_SCALE.length - 1].c;
  }

  function rgb(c, a = 1) {
    return `rgba(${c[0]},${c[1]},${c[2]},${a})`;
  }

  class SpatialHash {
    constructor(cellSize) {
      this.cellSize = cellSize;
      this.grid = new Map();
    }
    key(x, y) {
      const cx = Math.floor(x / this.cellSize);
      const cy = Math.floor(y / this.cellSize);
      return cx * 73856093 ^ cy * 19349663;
    }
    insert(hotspot) {
      const k = this.key(hotspot.sx, hotspot.sy);
      if (!this.grid.has(k)) this.grid.set(k, []);
      this.grid.get(k).push(hotspot);
    }
    query(x, y, radius) {
      const hits = [];
      const cx = Math.floor(x / this.cellSize);
      const cy = Math.floor(y / this.cellSize);
      const range = Math.ceil(radius / this.cellSize);
      for (let dx = -range; dx <= range; dx++) {
        for (let dy = -range; dy <= range; dy++) {
          const k = (cx + dx) * 73856093 ^ (cy + dy) * 19349663;
          const list = this.grid.get(k);
          if (list) hits.push(...list);
        }
      }
      return hits;
    }
    clear() { this.grid.clear(); }
  }

  class ClayImage {
    constructor(canvas, options = {}) {
      this.canvas = typeof canvas === 'string' ? document.querySelector(canvas) : canvas;
      if (!this.canvas) throw new Error('[ClayImage] Canvas element not found');
      this.ctx = this.canvas.getContext('2d');
      this.options = Object.assign({}, DEFAULT_OPTIONS, options);
      this.dpr = Math.max(1, window.devicePixelRatio || 1);
      this.hotspots = [];
      this.sensors = [];
      this.spatialHash = new SpatialHash(32);
      this.hovered = null;
      this.tooltipTimer = null;

      this._initEvents();
      this._initTooltip();
      this.resize();
    }

    resize(width, height) {
      const cssW = width || this.canvas.clientWidth || 600;
      const cssH = height || this.canvas.clientHeight || 500;
      this.cssW = cssW;
      this.cssH = cssH;
      this.canvas.width = Math.floor(cssW * this.dpr);
      this.canvas.height = Math.floor(cssH * this.dpr);
      this.ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
      this._buildSculptureContour();
      this.render();
    }

    _buildSculptureContour() {
      const cx = this.cssW / 2;
      const cy = this.cssH / 2 + 20;
      this.center = { cx, cy };

      const t = this.options.sculptureType;
      if (t === 'Buddha' || t === '人像') {
        this.contour = [
          { x: cx, y: cy - 180, r: 55, label: '头部' },
          { x: cx, y: cy - 70,  rx: 85, ry: 110, label: '躯干' },
          { x: cx - 80, y: cy + 40, rx: 35, ry: 75, label: '左臂' },
          { x: cx + 80, y: cy + 40, rx: 35, ry: 75, label: '右臂' },
          { x: cx, y: cy + 140, rx: 100, ry: 45, label: '底座' }
        ];
      } else {
        this.contour = [
          { x: cx, y: cy, rx: Math.min(cx, cy) * 0.85, ry: Math.min(cx, cy) * 0.75, label: '器身' }
        ];
      }
    }

    updateHotspots(hotspots) {
      this.hotspots = hotspots || [];
      this.spatialHash.clear();

      for (let i = 0; i < this.hotspots.length; i++) {
        const h = this.hotspots[i];
        h.sx = h.x * this.cssW;
        h.sy = h.y * this.cssH;
        h._radius = this.options.hotSpotMinRadius
          + (this.options.hotSpotMaxRadius - this.options.hotSpotMinRadius)
          * Math.pow((h.intensity || 50) / 100, 1.5);
        h._color = lerpColor((h.intensity || 50) / 100);
        h._level = (h.intensity || 0) >= this.options.thresholdCritical
          ? 'critical'
          : (h.intensity || 0) >= this.options.thresholdWarning ? 'warning' : 'normal';
        this.spatialHash.insert(h);
      }
    }

    updateSensors(sensors) {
      this.sensors = sensors || [];
    }

    render() {
      const { ctx, cssW, cssH } = this;
      ctx.clearRect(0, 0, cssW, cssH);

      this._drawBackground();
      this._drawSculptureContour();
      this._drawHeatmapLayer();
      this._drawSaltHotspots();
      this._drawSensorMarkers();
      this._drawTitle();
      if (this.options.showConcentrationScale) this._drawConcentrationScale();
    }

    _drawBackground() {
      const { ctx, cssW, cssH, options } = this;
      const bg = ctx.createRadialGradient(
        cssW / 2, cssH / 2, cssW * 0.1,
        cssW / 2, cssH / 2, cssW * 0.8
      );
      bg.addColorStop(0, '#FBF5E6');
      bg.addColorStop(1, '#E8DCC3');
      ctx.fillStyle = bg;
      ctx.fillRect(0, 0, cssW, cssH);

      ctx.strokeStyle = 'rgba(139,69,19,0.08)';
      ctx.lineWidth = 1;
      const step = 32;
      for (let x = 0; x < cssW; x += step) {
        ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x, cssH); ctx.stroke();
      }
      for (let y = 0; y < cssH; y += step) {
        ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(cssW, y); ctx.stroke();
      }
    }

    _drawSculptureContour() {
      const { ctx, options, contour } = this;

      for (const part of contour) {
        ctx.save();
        const grad = ctx.createRadialGradient(
          part.x, part.y, 5,
          part.x, part.y, Math.max(part.rx || part.r, part.ry || part.r)
        );
        grad.addColorStop(0, options.baseColor);
        grad.addColorStop(0.7, shadeColor(options.baseColor, -10));
        grad.addColorStop(1, shadeColor(options.baseColor, -25));

        ctx.fillStyle = grad;
        ctx.strokeStyle = options.outlineColor;
        ctx.lineWidth = 2.5;

        ctx.beginPath();
        if (part.r) {
          ctx.arc(part.x, part.y, part.r, 0, Math.PI * 2);
        } else {
          ctx.ellipse(part.x, part.y, part.rx, part.ry, 0, 0, Math.PI * 2);
        }
        ctx.fill();
        ctx.stroke();
        ctx.restore();
      }

      this._drawPaintPatterns();
    }

    _drawPaintPatterns() {
      const { ctx, center } = this;
      ctx.save();
      ctx.globalAlpha = 0.3;
      const colors = ['#C41E3A', '#1F4788', '#DAA520', '#2E8B57'];
      for (let i = 0; i < 8; i++) {
        const angle = (i / 8) * Math.PI * 2 - Math.PI / 2;
        const r = 90;
        const x = center.cx + Math.cos(angle) * r;
        const y = center.cy - 40 + Math.sin(angle) * r * 0.5;
        ctx.fillStyle = colors[i % colors.length];
        ctx.beginPath();
        ctx.arc(x, y, 8, 0, Math.PI * 2);
        ctx.fill();
      }
      ctx.restore();
    }

    _drawHeatmapLayer() {
      const { ctx, cssW, cssH, hotspots, options } = this;
      if (hotspots.length === 0) return;

      const offscreen = document.createElement('canvas');
      offscreen.width = cssW;
      offscreen.height = cssH;
      const octx = offscreen.getContext('2d');

      for (const h of hotspots) {
        const radius = h._radius * 2.2;
        const g = octx.createRadialGradient(h.sx, h.sy, 0, h.sx, h.sy, radius);
        g.addColorStop(0, rgb(h._color, 0.85));
        g.addColorStop(0.4, rgb(h._color, 0.4));
        g.addColorStop(1, rgb(h._color, 0));
        octx.fillStyle = g;
        octx.fillRect(h.sx - radius, h.sy - radius, radius * 2, radius * 2);
      }

      ctx.save();
      ctx.globalAlpha = options.heatmapOpacity;
      ctx.drawImage(offscreen, 0, 0);
      ctx.restore();
    }

    _drawSaltHotspots() {
      const { ctx, hotspots, options } = this;
      for (const h of hotspots) {
        if (options.hotspotGlow && h._level !== 'normal') {
          ctx.save();
          ctx.shadowColor = rgb(h._color, 1);
          ctx.shadowBlur = h._level === 'critical' ? 28 : 14;
          ctx.fillStyle = rgb(h._color, 0.9);
          ctx.beginPath();
          ctx.arc(h.sx, h.sy, h._radius * 0.7, 0, Math.PI * 2);
          ctx.fill();
          ctx.restore();
        }

        const core = ctx.createRadialGradient(h.sx, h.sy, 0, h.sx, h.sy, h._radius);
        core.addColorStop(0, '#FFFFFF');
        core.addColorStop(0.25, rgb(h._color, 0.95));
        core.addColorStop(1, rgb(h._color, 0));
        ctx.fillStyle = core;
        ctx.beginPath();
        ctx.arc(h.sx, h.sy, h._radius, 0, Math.PI * 2);
        ctx.fill();

        const ringColor = h._level === 'critical' ? '#DC143C'
          : h._level === 'warning' ? '#FFD700' : 'rgba(255,255,255,0.6)';
        ctx.strokeStyle = ringColor;
        ctx.lineWidth = h._level === 'normal' ? 1 : 2;
        ctx.setLineDash(h._level === 'critical' ? [] : [5, 3]);
        ctx.beginPath();
        ctx.arc(h.sx, h.sy, h._radius + 4, 0, Math.PI * 2);
        ctx.stroke();
        ctx.setLineDash([]);
      }
    }

    _drawSensorMarkers() {
      if (!this.options.showSensorMarkers) return;
      const { ctx, sensors, cssW, cssH } = this;
      for (const s of sensors) {
        const sx = (s.x || 0.5) * cssW;
        const sy = (s.y || 0.5) * cssH;
        ctx.save();
        ctx.fillStyle = s.type === 'ion' ? '#4169E1' : '#2E8B57';
        ctx.strokeStyle = '#fff';
        ctx.lineWidth = 1.5;
        ctx.beginPath();
        ctx.moveTo(sx, sy - 10);
        ctx.lineTo(sx + 8, sy + 6);
        ctx.lineTo(sx - 8, sy + 6);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
        ctx.fillStyle = '#5D4037';
        ctx.font = '10px sans-serif';
        ctx.textAlign = 'center';
        ctx.fillText(s.code || s.id || '', sx, sy + 18);
        ctx.restore();
      }
    }

    _drawTitle() {
      const { ctx, cssW, options } = this;
      ctx.save();
      ctx.fillStyle = '#5D4037';
      ctx.font = 'bold 16px "Noto Serif SC", serif';
      ctx.textAlign = 'center';
      ctx.fillText(options.sculptureName, cssW / 2, 28);
      ctx.font = '11px sans-serif';
      ctx.fillStyle = 'rgba(93,64,55,0.6)';
      ctx.fillText(`盐分热点监测 · ${new Date().toLocaleString('zh-CN')}`, cssW / 2, 46);
      ctx.restore();
    }

    _drawConcentrationScale() {
      const { ctx, cssW, cssH } = this;
      const barW = 220, barH = 14;
      const ox = cssW - barW - 24, oy = cssH - 44;
      ctx.save();
      const grad = ctx.createLinearGradient(ox, oy, ox + barW, oy);
      for (const stop of COLOR_SCALE) {
        grad.addColorStop(stop.v / 100, rgb(stop.c, 1));
      }
      ctx.fillStyle = grad;
      ctx.fillRect(ox, oy, barW, barH);
      ctx.strokeStyle = 'rgba(139,69,19,0.5)';
      ctx.lineWidth = 1;
      ctx.strokeRect(ox, oy, barW, barH);
      ctx.fillStyle = '#5D4037';
      ctx.font = '10px sans-serif';
      ctx.textAlign = 'left';
      const ticks = [0, 25, 50, 75, 100];
      for (const t of ticks) {
        const tx = ox + (t / 100) * barW;
        ctx.beginPath();
        ctx.moveTo(tx, oy + barH);
        ctx.lineTo(tx, oy + barH + 4);
        ctx.stroke();
        ctx.fillText(t + 'ppm', tx - 10, oy + barH + 16);
      }
      ctx.textAlign = 'right';
      ctx.font = 'bold 11px sans-serif';
      ctx.fillText('盐分富集浓度', ox + barW, oy - 4);
      ctx.restore();
    }

    _initTooltip() {
      this.tooltip = document.createElement('div');
      Object.assign(this.tooltip.style, {
        position: 'fixed',
        background: 'rgba(255,255,255,0.98)',
        border: '1px solid rgba(139,69,19,0.3)',
        borderRadius: '6px',
        padding: '8px 12px',
        boxShadow: '0 4px 14px rgba(139,69,19,0.18)',
        fontSize: '12px',
        pointerEvents: 'none',
        zIndex: '9999',
        display: 'none',
        minWidth: '140px',
        color: '#5D4037'
      });
      document.body.appendChild(this.tooltip);
    }

    _initEvents() {
      this._onMove = (e) => {
        const rect = this.canvas.getBoundingClientRect();
        const mx = e.clientX - rect.left;
        const my = e.clientY - rect.top;

        clearTimeout(this.tooltipTimer);
        this.tooltipTimer = setTimeout(() => {
          const matches = this.spatialHash.query(mx, my, 40);
          let hit = null; let minDist = Infinity;
          for (const h of matches) {
            const d = Math.hypot(h.sx - mx, h.sy - my);
            if (d <= h._radius + 6 && d < minDist) {
              minDist = d; hit = h;
            }
          }

          if (hit) {
            this.hovered = hit;
            this._showTooltip(e.clientX, e.clientY, hit);
            this.canvas.style.cursor = 'pointer';
          } else {
            this.hovered = null;
            this.tooltip.style.display = 'none';
            this.canvas.style.cursor = 'default';
          }
        }, this.options.tooltipDelay);
      };
      this._onLeave = () => {
        clearTimeout(this.tooltipTimer);
        this.tooltip.style.display = 'none';
        this.canvas.style.cursor = 'default';
      };
      this.canvas.addEventListener('mousemove', this._onMove);
      this.canvas.addEventListener('mouseleave', this._onLeave);
    }

    _showTooltip(x, y, h) {
      const levelText = h._level === 'critical' ? '⚠ 超标'
        : h._level === 'warning' ? '⚠ 预警' : '正常';
      const levelColor = h._level === 'critical' ? '#DC143C'
        : h._level === 'warning' ? '#DAA520' : '#2E8B57';
      this.tooltip.innerHTML = `
        <div style="font-weight:bold;margin-bottom:4px;color:${levelColor}">${levelText}</div>
        <div style="margin-bottom:2px">${h.label || '热点'}</div>
        <div>强度：<b>${Math.round(h.intensity || 0)}</b> / 100</div>
        ${h.zone ? `<div>区域：${h.zone}</div>` : ''}
        <div style="margin-top:4px;border-top:1px solid rgba(139,69,19,0.15);padding-top:4px;font-size:11px;color:#999">
          坐标: (${(h.x*100).toFixed(1)}%, ${(h.y*100).toFixed(1)}%)
        </div>
      `;
      this.tooltip.style.display = 'block';
      const tipW = this.tooltip.offsetWidth;
      const tipH = this.tooltip.offsetHeight;
      let px = x + 14, py = y + 14;
      if (px + tipW > window.innerWidth) px = x - tipW - 14;
      if (py + tipH > window.innerHeight) py = y - tipH - 14;
      this.tooltip.style.left = px + 'px';
      this.tooltip.style.top = py + 'px';
    }

    destroy() {
      this.canvas.removeEventListener('mousemove', this._onMove);
      this.canvas.removeEventListener('mouseleave', this._onLeave);
      if (this.tooltip && this.tooltip.parentNode) this.tooltip.parentNode.removeChild(this.tooltip);
    }

    getStatistics() {
      if (this.hotspots.length === 0) return { count: 0, critical: 0, warning: 0, avgIntensity: 0 };
      let critical = 0, warning = 0, sum = 0;
      for (const h of this.hotspots) {
        const i = h.intensity || 0;
        sum += i;
        if (i >= this.options.thresholdCritical) critical++;
        else if (i >= this.options.thresholdWarning) warning++;
      }
      return {
        count: this.hotspots.length,
        critical, warning,
        normal: this.hotspots.length - critical - warning,
        avgIntensity: Math.round(sum / this.hotspots.length),
        maxIntensity: Math.round(Math.max(...this.hotspots.map(h => h.intensity || 0)))
      };
    }
  }

  function shadeColor(color, percent) {
    const num = parseInt(color.replace('#', ''), 16);
    const amt = Math.round(2.55 * percent);
    const R = (num >> 16) + amt;
    const G = (num >> 8 & 0x00FF) + amt;
    const B = (num & 0x0000FF) + amt;
    return '#' + (
      0x1000000
      + (R < 255 ? (R < 1 ? 0 : R) : 255) * 0x10000
      + (G < 255 ? (G < 1 ? 0 : G) : 255) * 0x100
      + (B < 255 ? (B < 1 ? 0 : B) : 255)
    ).toString(16).slice(1);
  }

  ClayImage.COLOR_SCALE = COLOR_SCALE;
  ClayImage.DEFAULT_OPTIONS = DEFAULT_OPTIONS;

  if (typeof module !== 'undefined' && module.exports) {
    module.exports = ClayImage;
  } else {
    global.ClayImage = ClayImage;
  }

})(typeof window !== 'undefined' ? window : this);
