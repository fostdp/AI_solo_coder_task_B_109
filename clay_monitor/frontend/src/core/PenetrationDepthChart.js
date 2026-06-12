class PenetrationDepthChart {
    constructor(containerId, options = {}) {
        this.container = document.getElementById(containerId);
        if (!this.container) throw new Error(`Container ${containerId} not found`);

        this.options = {
            width: options.width || 600,
            height: options.height || 400,
            dpr: window.devicePixelRatio || 1,
            maxDepth: options.maxDepth || 10,
            showGrid: options.showGrid !== false,
            showLegend: options.showLegend !== false,
            showTooltip: options.showTooltip !== false,
            ...options
        };

        this.canvas = document.createElement('canvas');
        this.canvas.style.width = this.options.width + 'px';
        this.canvas.style.height = this.options.height + 'px';
        this.canvas.width = this.options.width * this.options.dpr;
        this.canvas.height = this.options.height * this.options.dpr;
        this.ctx = this.canvas.getContext('2d');
        this.ctx.scale(this.options.dpr, this.options.dpr);

        this.container.appendChild(this.canvas);

        this.margin = { top: 40, right: 120, bottom: 50, left: 70 };
        this.plotWidth = this.options.width - this.margin.left - this.margin.right;
        this.plotHeight = this.options.height - this.margin.top - this.margin.bottom;

        this.data = null;
        this.hoveredIndex = -1;

        this._setupInteraction();
    }

    setData(penetrationResults) {
        if (!Array.isArray(penetrationResults)) {
            penetrationResults = [penetrationResults];
        }
        this.data = penetrationResults;
        this.render();
    }

    render() {
        if (!this.data || this.data.length === 0) return;

        const ctx = this.ctx;
        ctx.clearRect(0, 0, this.options.width, this.options.height);

        this._drawBackground();
        this._drawAxes();
        if (this.options.showGrid) this._drawGrid();

        const colors = ['#3b82f6', '#10b981', '#f59e0b', '#ef4444', '#8b5cf6'];

        this.data.forEach((result, index) => {
            this._drawDepthProfile(result, colors[index % colors.length], index);
        });

        if (this.options.showLegend) this._drawLegend();
        if (this.options.showTooltip && this.hoveredIndex >= 0) this._drawTooltip();
    }

    _drawBackground() {
        const ctx = this.ctx;
        const gradient = ctx.createLinearGradient(0, this.margin.top, 0, this.options.height - this.margin.bottom);
        gradient.addColorStop(0, 'rgba(255, 255, 255, 0.1)');
        gradient.addColorStop(1, 'rgba(255, 255, 255, 0.05)');
        ctx.fillStyle = gradient;
        ctx.fillRect(this.margin.left, this.margin.top, this.plotWidth, this.plotHeight);
    }

    _drawAxes() {
        const ctx = this.ctx;

        ctx.strokeStyle = '#374151';
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(this.margin.left, this.margin.top);
        ctx.lineTo(this.margin.left, this.options.height - this.margin.bottom);
        ctx.lineTo(this.options.width - this.margin.right, this.options.height - this.margin.bottom);
        ctx.stroke();

        ctx.fillStyle = '#6b7280';
        ctx.font = '12px system-ui, sans-serif';
        ctx.textAlign = 'center';

        for (let i = 0; i <= 5; i++) {
            const x = this.margin.left + (i / 5) * this.plotWidth;
            const time = (i / 5) * 3600;
            ctx.fillText(Math.round(time / 60) + '分', x, this.options.height - this.margin.bottom + 20);
        }

        ctx.save();
        ctx.translate(20, this.margin.top + this.plotHeight / 2);
        ctx.rotate(-Math.PI / 2);
        ctx.fillText('渗透深度 (mm)', 0, 0);
        ctx.restore();

        ctx.fillStyle = '#111827';
        ctx.font = 'bold 14px system-ui, sans-serif';
        ctx.textAlign = 'center';
        ctx.fillText('渗透深度预测', this.options.width / 2, 25);

        ctx.fillStyle = '#6b7280';
        ctx.font = '12px system-ui, sans-serif';
        ctx.fillText('时间', this.options.width / 2, this.options.height - 15);

        ctx.textAlign = 'right';
        for (let i = 0; i <= 5; i++) {
            const y = this.options.height - this.margin.bottom - (i / 5) * this.plotHeight;
            const depth = (i / 5) * this.options.maxDepth;
            ctx.fillText(depth.toFixed(1), this.margin.left - 10, y + 4);

            ctx.strokeStyle = '#d1d5db';
            ctx.lineWidth = 1;
            ctx.setLineDash([3, 3]);
            ctx.beginPath();
            ctx.moveTo(this.margin.left, y);
            ctx.lineTo(this.options.width - this.margin.right, y);
            ctx.stroke();
            ctx.setLineDash([]);
        }
    }

    _drawGrid() {
        const ctx = this.ctx;
        ctx.strokeStyle = '#e5e7eb';
        ctx.lineWidth = 1;

        for (let i = 1; i < 5; i++) {
            const x = this.margin.left + (i / 5) * this.plotWidth;
            ctx.beginPath();
            ctx.moveTo(x, this.margin.top);
            ctx.lineTo(x, this.options.height - this.margin.bottom);
            ctx.stroke();
        }
    }

    _drawDepthProfile(result, color, seriesIndex) {
        if (!result.DepthProfile || !result.TimePoints) return;

        const ctx = this.ctx;
        const { DepthProfile, TimePoints } = result;

        const points = [];
        for (let i = 0; i < Math.min(DepthProfile.length, TimePoints.length); i++) {
            const x = this.margin.left + (TimePoints[i] / Math.max(...TimePoints, 3600)) * this.plotWidth;
            const y = this.options.height - this.margin.bottom - (DepthProfile[i] / this.options.maxDepth) * this.plotHeight;
            points.push({ x, y, depth: DepthProfile[i], time: TimePoints[i] });
        }

        if (points.length < 2) return;

        const gradient = ctx.createLinearGradient(0, this.margin.top, 0, this.options.height - this.margin.bottom);
        gradient.addColorStop(0, color + '40');
        gradient.addColorStop(1, color + '05');
        ctx.fillStyle = gradient;
        ctx.beginPath();
        ctx.moveTo(points[0].x, this.options.height - this.margin.bottom);
        points.forEach(p => ctx.lineTo(p.x, p.y));
        ctx.lineTo(points[points.length - 1].x, this.options.height - this.margin.bottom);
        ctx.closePath();
        ctx.fill();

        ctx.strokeStyle = color;
        ctx.lineWidth = 2.5;
        ctx.beginPath();
        points.forEach((p, i) => {
            if (i === 0) ctx.moveTo(p.x, p.y);
            else ctx.lineTo(p.x, p.y);
        });
        ctx.stroke();

        const lastPoint = points[points.length - 1];
        if (lastPoint) {
            ctx.fillStyle = color;
            ctx.beginPath();
            ctx.arc(lastPoint.x, lastPoint.y, 6, 0, Math.PI * 2);
            ctx.fill();

            ctx.fillStyle = '#1f2937';
            ctx.font = 'bold 11px system-ui, sans-serif';
            ctx.textAlign = 'left';
            ctx.fillText(`${result.PredictedDepthMm?.toFixed(2) || 'N/A'}mm`, lastPoint.x + 10, lastPoint.y - 5);
        }

        if (this.hoveredIndex === seriesIndex && this.hoveredPointIndex >= 0 && this.hoveredPointIndex < points.length) {
            const p = points[this.hoveredPointIndex];
            ctx.strokeStyle = color;
            ctx.lineWidth = 2;
            ctx.setLineDash([4, 4]);
            ctx.beginPath();
            ctx.moveTo(this.margin.left, p.y);
            ctx.lineTo(p.x, p.y);
            ctx.lineTo(p.x, this.options.height - this.margin.bottom);
            ctx.stroke();
            ctx.setLineDash([]);

            ctx.fillStyle = color;
            ctx.beginPath();
            ctx.arc(p.x, p.y, 8, 0, Math.PI * 2);
            ctx.fill();
            ctx.strokeStyle = '#fff';
            ctx.lineWidth = 2;
            ctx.stroke();
        }
    }

    _drawLegend() {
        const ctx = this.ctx;
        const colors = ['#3b82f6', '#10b981', '#f59e0b', '#ef4444', '#8b5cf6'];

        const legendX = this.options.width - this.margin.right + 15;
        let legendY = this.margin.top;

        ctx.font = '11px system-ui, sans-serif';

        this.data.forEach((result, index) => {
            const color = colors[index % colors.length];

            ctx.fillStyle = color;
            ctx.beginPath();
            ctx.arc(legendX + 6, legendY + 6, 5, 0, Math.PI * 2);
            ctx.fill();

            ctx.fillStyle = '#374151';
            ctx.textAlign = 'left';
            const name = result.MaterialName || '材料 ' + (index + 1);
            const shortName = name.length > 10 ? name.substring(0, 10) + '...' : name;
            ctx.fillText(shortName, legendX + 18, legendY + 10);

            ctx.fillStyle = '#6b7280';
            ctx.font = '10px system-ui, sans-serif';
            ctx.fillText(`${result.PredictedDepthMm?.toFixed(2) || 'N/A'}mm`, legendX + 18, legendY + 23);

            const grade = result.PenetrationGrade || '';
            const gradeColor = this._getGradeColor(grade);
            ctx.fillStyle = gradeColor;
            ctx.fillText(grade, legendX + 18, legendY + 35);

            legendY += 45;
        });
    }

    _getGradeColor(grade) {
        const colors = {
            'EXCELLENT': '#059669',
            'GOOD': '#0284c7',
            'FAIR': '#d97706',
            'POOR': '#dc2626',
            'INADEQUATE': '#991b1b'
        };
        return colors[grade] || '#6b7280';
    }

    _drawTooltip() {
        if (!this.data || this.hoveredIndex >= this.data.length) return;

        const result = this.data[this.hoveredIndex];
        if (!result || !result.TimePoints || !result.DepthProfile) return;

        const idx = Math.min(this.hoveredPointIndex, result.TimePoints.length - 1, result.DepthProfile.length - 1);
        if (idx < 0) return;

        const time = result.TimePoints[idx];
        const depth = result.DepthProfile[idx];

        const ctx = this.ctx;
        const padding = 12;
        const lineHeight = 18;

        const lines = [
            `材料: ${result.MaterialName || '未知'}`,
            `时间: ${(time / 60).toFixed(1)}分钟`,
            `深度: ${depth.toFixed(3)}mm`,
            `速率: ${result.PenetrationRateMmPerS?.toFixed(4) || 'N/A'} mm/s`,
            `毛细管压力: ${result.CapillaryPressurePa?.toFixed(1) || 'N/A'} Pa`,
            `等级: ${result.PenetrationGrade || 'N/A'}`
        ];

        const maxWidth = Math.max(...lines.map(l => ctx.measureText(l).width));
        const tooltipWidth = maxWidth + padding * 2;
        const tooltipHeight = lines.length * lineHeight + padding * 2;

        let x = this.mouseX + 15;
        let y = this.mouseY + 15;

        if (x + tooltipWidth > this.options.width) x = this.mouseX - tooltipWidth - 15;
        if (y + tooltipHeight > this.options.height) y = this.mouseY - tooltipHeight - 15;

        ctx.fillStyle = 'rgba(17, 24, 39, 0.95)';
        ctx.beginPath();
        ctx.roundRect(x, y, tooltipWidth, tooltipHeight, 8);
        ctx.fill();

        ctx.strokeStyle = 'rgba(75, 85, 99, 0.5)';
        ctx.lineWidth = 1;
        ctx.stroke();

        ctx.fillStyle = '#fff';
        ctx.font = '12px system-ui, sans-serif';
        ctx.textAlign = 'left';
        lines.forEach((line, i) => {
            ctx.fillText(line, x + padding, y + padding + (i + 0.8) * lineHeight);
        });
    }

    _setupInteraction() {
        this.canvas.addEventListener('mousemove', (e) => {
            const rect = this.canvas.getBoundingClientRect();
            this.mouseX = (e.clientX - rect.left) / (rect.width / this.options.width);
            this.mouseY = (e.clientY - rect.top) / (rect.height / this.options.height);

            this._updateHovered();
            this.render();
        });

        this.canvas.addEventListener('mouseleave', () => {
            this.hoveredIndex = -1;
            this.hoveredPointIndex = -1;
            this.render();
        });
    }

    _updateHovered() {
        if (!this.data || this.data.length === 0) return;
        if (this.mouseX < this.margin.left || this.mouseX > this.options.width - this.margin.right) {
            this.hoveredIndex = -1;
            this.hoveredPointIndex = -1;
            return;
        }

        const xRatio = (this.mouseX - this.margin.left) / this.plotWidth;
        let minDist = Infinity;

        this.data.forEach((result, seriesIndex) => {
            if (!result.TimePoints) return;
            const maxTime = Math.max(...result.TimePoints, 3600);
            const time = xRatio * maxTime;

            result.TimePoints.forEach((t, pointIndex) => {
                const dist = Math.abs(t - time);
                if (dist < minDist) {
                    minDist = dist;
                    this.hoveredIndex = seriesIndex;
                    this.hoveredPointIndex = pointIndex;
                }
            });
        });
    }

    resize(width, height) {
        this.options.width = width;
        this.options.height = height;
        this.canvas.style.width = width + 'px';
        this.canvas.style.height = height + 'px';
        this.canvas.width = width * this.options.dpr;
        this.canvas.height = height * this.options.dpr;
        this.ctx.scale(this.options.dpr, this.options.dpr);
        this.plotWidth = width - this.margin.left - this.margin.right;
        this.plotHeight = height - this.margin.top - this.margin.bottom;
        this.render();
    }
}

if (typeof module !== 'undefined' && module.exports) {
    module.exports = PenetrationDepthChart;
}
