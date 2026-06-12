class VirtualReinforcement3D {
    constructor(containerId, options = {}) {
        this.container = document.getElementById(containerId);
        if (!this.container) throw new Error(`Container ${containerId} not found`);

        this.options = {
            width: options.width || 700,
            height: options.height || 500,
            dpr: window.devicePixelRatio || 1,
            interactive: options.interactive !== false,
            ...options
        };

        this.canvas = document.createElement('canvas');
        this.canvas.style.cssText = `
            width: ${this.options.width}px;
            height: ${this.options.height}px;
            border-radius: 12px;
            box-shadow: 0 4px 20px rgba(0,0,0,0.1);
            cursor: ${this.options.interactive ? 'grab' : 'default'};
        `;
        this.canvas.width = this.options.width * this.options.dpr;
        this.canvas.height = this.options.height * this.options.dpr;
        this.ctx = this.canvas.getContext('2d');
        this.ctx.scale(this.options.dpr, this.options.dpr);

        this.container.appendChild(this.canvas);

        this.rotationX = 0.4;
        this.rotationY = 0.6;
        this.zoom = 1.0;
        this.isDragging = false;
        this.lastX = 0;
        this.lastY = 0;

        this.data = null;
        this.viewMode = 'PENETRATION';

        if (this.options.interactive) {
            this._setupInteraction();
        }

        this._renderLoop();
    }

    setData(result) {
        this.data = result;
    }

    setViewMode(mode) {
        this.viewMode = mode;
    }

    _setupInteraction() {
        this.canvas.addEventListener('mousedown', (e) => {
            this.isDragging = true;
            this.lastX = e.clientX;
            this.lastY = e.clientY;
            this.canvas.style.cursor = 'grabbing';
        });

        document.addEventListener('mousemove', (e) => {
            if (!this.isDragging) return;
            const dx = e.clientX - this.lastX;
            const dy = e.clientY - this.lastY;
            this.rotationY += dx * 0.005;
            this.rotationX += dy * 0.005;
            this.rotationX = Math.max(-1.2, Math.min(1.2, this.rotationX));
            this.lastX = e.clientX;
            this.lastY = e.clientY;
        });

        document.addEventListener('mouseup', () => {
            this.isDragging = false;
            this.canvas.style.cursor = 'grab';
        });

        this.canvas.addEventListener('wheel', (e) => {
            e.preventDefault();
            this.zoom *= e.deltaY > 0 ? 0.95 : 1.05;
            this.zoom = Math.max(0.5, Math.min(2.0, this.zoom));
        }, { passive: false });

        this.canvas.addEventListener('mouseleave', () => {
            this.isDragging = false;
            this.canvas.style.cursor = 'grab';
        });
    }

    _renderLoop() {
        const animate = () => {
            this._render();
            requestAnimationFrame(animate);
        };
        animate();
    }

    _render() {
        const ctx = this.ctx;
        const { width, height } = this.options;

        const bgGradient = ctx.createLinearGradient(0, 0, 0, height);
        bgGradient.addColorStop(0, '#f8fafc');
        bgGradient.addColorStop(1, '#e2e8f0');
        ctx.fillStyle = bgGradient;
        ctx.fillRect(0, 0, width, height);

        this._drawGrid();

        if (this.data && this.data.Voxels) {
            this._drawVoxels();
        } else {
            this._drawPlaceholder();
        }

        if (this.data) {
            this._drawColorBar();
            this._drawInfoPanel();
        }

        this._drawControlsHint();
    }

    _drawGrid() {
        const ctx = this.ctx;
        const { width, height } = this.options;
        const centerX = width / 2;
        const centerY = height / 2 + 30;

        ctx.strokeStyle = 'rgba(148, 163, 184, 0.3)';
        ctx.lineWidth = 1;

        const gridSize = 40;
        for (let x = -4; x <= 4; x++) {
            for (let z = -4; z <= 4; z++) {
                const p1 = this._project(x * gridSize, 0, z * gridSize, centerX, centerY);
                const p2 = this._project((x + 1) * gridSize, 0, z * gridSize, centerX, centerY);
                const p3 = this._project(x * gridSize, 0, (z + 1) * gridSize, centerX, centerY);

                ctx.beginPath();
                ctx.moveTo(p1.x, p1.y);
                ctx.lineTo(p2.x, p2.y);
                ctx.moveTo(p1.x, p1.y);
                ctx.lineTo(p3.x, p3.y);
                ctx.stroke();
            }
        }
    }

    _project(x, y, z, centerX, centerY) {
        const cosX = Math.cos(this.rotationX);
        const sinX = Math.sin(this.rotationX);
        const cosY = Math.cos(this.rotationY);
        const sinY = Math.sin(this.rotationY);

        let x1 = x * cosY - z * sinY;
        let z1 = x * sinY + z * cosY;
        let y1 = y;

        let y2 = y1 * cosX - z1 * sinX;
        let z2 = y1 * sinX + z1 * cosX;

        const perspective = 800 * this.zoom;
        const scale = perspective / (perspective + z2);

        return {
            x: centerX + x1 * scale,
            y: centerY - y2 * scale,
            z: z2,
            scale: scale
        };
    }

    _drawVoxels() {
        const ctx = this.ctx;
        const { width, height } = this.options;
        const centerX = width / 2;
        const centerY = height / 2 + 30;

        const voxels = this.data.Voxels;
        const maxX = this.options.DefaultWidthCm || 40;
        const maxY = this.options.DefaultHeightCm || 60;
        const maxZ = this.options.DefaultThicknessCm || 5;

        const scale = Math.min(width, height) / Math.max(maxX, maxY) * 0.6 * this.zoom;

        const projected = voxels.map((v, idx) => {
            const sx = (v.X - maxX / 2) * scale;
            const sy = (maxY - v.Y) * scale;
            const sz = (v.Z - maxZ / 2) * scale;
            const p = this._project(sx, sy, sz, centerX, centerY);
            return { ...v, idx, ...p, size: 6 * p.scale };
        });

        projected.sort((a, b) => b.z - a.z);

        projected.forEach(v => {
            if (!v.IsReinforced && v.Concentration < 0.05) {
                ctx.fillStyle = 'rgba(148, 163, 184, 0.15)';
                ctx.strokeStyle = 'rgba(148, 163, 184, 0.3)';
            } else {
                const color = this._getVoxelColor(v);
                const alpha = 0.4 + v.Concentration * 0.6;
                ctx.fillStyle = color.replace(')', `, ${alpha})`).replace('rgb', 'rgba');
                ctx.strokeStyle = color;
            }

            ctx.lineWidth = 0.5;

            ctx.beginPath();
            ctx.arc(v.x, v.y, v.size, 0, Math.PI * 2);
            ctx.fill();
            if (v.Concentration > 0.3) {
                ctx.stroke();
            }
        });

        if (this.data.IsoSurfaces && this.data.IsoSurfaces.length > 0) {
            this._drawIsoSurfaces(centerX, centerY, scale, maxX, maxY, maxZ);
        }
    }

    _getVoxelColor(voxel) {
        let value;
        switch (this.viewMode) {
            case 'GLOSS':
                value = voxel.Gloss / 100;
                return this._interpolateColor(value, [
                    [0, '#1f2937'],
                    [0.5, '#fbbf24'],
                    [1, '#fef3c7']
                ]);
            case 'HARDNESS':
                value = (voxel.Hardness - 1) / 2;
                return this._interpolateColor(value, [
                    [0, '#60a5fa'],
                    [0.5, '#34d399'],
                    [1, '#fbbf24']
                ]);
            case 'PENETRATION':
            default:
                value = voxel.Concentration;
                return this._interpolateColor(value, [
                    [0, '#1e3a8a'],
                    [0.25, '#3b82f6'],
                    [0.5, '#10b981'],
                    [0.75, '#fbbf24'],
                    [1, '#ef4444']
                ]);
        }
    }

    _interpolateColor(t, stops) {
        t = Math.max(0, Math.min(1, t));
        for (let i = 0; i < stops.length - 1; i++) {
            const [t1, c1] = stops[i];
            const [t2, c2] = stops[i + 1];
            if (t >= t1 && t <= t2) {
                const localT = (t - t1) / (t2 - t1);
                return this._mixColors(c1, c2, localT);
            }
        }
        return stops[stops.length - 1][1];
    }

    _mixColors(c1, c2, t) {
        const r1 = parseInt(c1.slice(1, 3), 16);
        const g1 = parseInt(c1.slice(3, 5), 16);
        const b1 = parseInt(c1.slice(5, 7), 16);
        const r2 = parseInt(c2.slice(1, 3), 16);
        const g2 = parseInt(c2.slice(3, 5), 16);
        const b2 = parseInt(c2.slice(5, 7), 16);

        const r = Math.round(r1 + (r2 - r1) * t);
        const g = Math.round(g1 + (g2 - g1) * t);
        const b = Math.round(b1 + (b2 - b1) * t);

        return `rgb(${r}, ${g}, ${b})`;
    }

    _drawIsoSurfaces(centerX, centerY, scale, maxX, maxY, maxZ) {
        const ctx = this.ctx;
        const isoValues = this.data.IsoSurfaces;

        isoValues.forEach((depth, idx) => {
            const alpha = 0.3 - idx * 0.05;
            ctx.strokeStyle = `rgba(255, 255, 255, ${alpha})`;
            ctx.lineWidth = 2;
            ctx.setLineDash([5, 5]);

            const z = depth / 10;
            const zScaled = (z - maxZ / 2) * scale;

            const points = [];
            for (let i = 0; i <= 20; i++) {
                const angle = (i / 20) * Math.PI * 2;
                const rx = Math.cos(angle) * maxX * scale * 0.4;
                const ry = Math.sin(angle) * maxY * scale * 0.4;
                const p = this._project(rx, ry, zScaled, centerX, centerY);
                points.push(p);
            }

            ctx.beginPath();
            points.forEach((p, i) => {
                if (i === 0) ctx.moveTo(p.x, p.y);
                else ctx.lineTo(p.x, p.y);
            });
            ctx.stroke();
            ctx.setLineDash([]);
        });
    }

    _drawPlaceholder() {
        const ctx = this.ctx;
        const { width, height } = this.options;

        ctx.fillStyle = '#94a3b8';
        ctx.font = '14px system-ui';
        ctx.textAlign = 'center';
        ctx.fillText('选择加固材料开始虚拟模拟', width / 2, height / 2);

        ctx.strokeStyle = 'rgba(148, 163, 184, 0.3)';
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.arc(width / 2, height / 2, 80, 0, Math.PI * 2);
        ctx.stroke();
    }

    _drawColorBar() {
        const ctx = this.ctx;
        const { width, height } = this.options;

        const barX = width - 50;
        const barY = 60;
        const barW = 20;
        const barH = height - 120;

        const gradient = ctx.createLinearGradient(barX, barY + barH, barX, barY);
        const stops = [
            [0, '#1e3a8a'],
            [0.25, '#3b82f6'],
            [0.5, '#10b981'],
            [0.75, '#fbbf24'],
            [1, '#ef4444']
        ];
        stops.forEach(([pos, color]) => gradient.addColorStop(pos, color));

        ctx.fillStyle = gradient;
        ctx.fillRect(barX, barY, barW, barH);

        ctx.strokeStyle = '#475569';
        ctx.lineWidth = 1;
        ctx.strokeRect(barX, barY, barW, barH);

        ctx.fillStyle = '#374151';
        ctx.font = '10px system-ui';
        ctx.textAlign = 'left';

        const labels = this.viewMode === 'GLOSS' ? ['0', '50', '100'] :
                       this.viewMode === 'HARDNESS' ? ['1.0', '1.5', '2.5'] :
                       ['0', '0.5', '1.0'];

        labels.forEach((label, i) => {
            const y = barY + barH - (i / (labels.length - 1)) * barH;
            ctx.fillText(label, barX + barW + 8, y + 4);

            ctx.beginPath();
            ctx.moveTo(barX - 3, y);
            ctx.lineTo(barX, y);
            ctx.stroke();
        });

        const unitText = this.viewMode === 'GLOSS' ? '光泽度' :
                          this.viewMode === 'HARDNESS' ? '硬度' :
                          '浓度';
        ctx.fillStyle = '#6b7280';
        ctx.font = 'bold 11px system-ui';
        ctx.fillText(unitText, barX - 5, barY - 8);
    }

    _drawInfoPanel() {
        const ctx = this.ctx;
        const panelX = 20;
        const panelY = 20;
        const panelW = 200;
        const panelH = 160;

        ctx.fillStyle = 'rgba(255, 255, 255, 0.95)';
        ctx.beginPath();
        ctx.roundRect(panelX, panelY, panelW, panelH, 10);
        ctx.fill();

        ctx.strokeStyle = 'rgba(148, 163, 184, 0.3)';
        ctx.lineWidth = 1;
        ctx.stroke();

        ctx.fillStyle = '#1f2937';
        ctx.font = 'bold 13px system-ui';
        ctx.textAlign = 'left';
        ctx.fillText('加固效果预览', panelX + 15, panelY + 28);

        const material = this.data.MaterialName || '未知材料';
        const shortMaterial = material.length > 12 ? material.substring(0, 12) + '...' : material;

        const items = [
            { label: '材料', value: shortMaterial },
            { label: '平均渗透', value: `${this.data.AveragePenetrationDepthMm?.toFixed(2) || 'N/A'} mm` },
            { label: '最大渗透', value: `${this.data.MaximumPenetrationDepthMm?.toFixed(2) || 'N/A'} mm` },
            { label: '表面光泽', value: `${this.data.AverageSurfaceGloss?.toFixed(1) || 'N/A'}` },
            { label: '光泽变化', value: `${this.data.GlossChangePercent?.toFixed(1) || 'N/A'}%` },
            { label: '加固体积', value: `${this.data.ReinforcedVolumePercent?.toFixed(1) || 'N/A'}%` }
        ];

        ctx.font = '11px system-ui';
        items.forEach((item, i) => {
            const y = panelY + 50 + i * 18;
            ctx.fillStyle = '#6b7280';
            ctx.fillText(item.label, panelX + 15, y);
            ctx.fillStyle = '#1e40af';
            ctx.textAlign = 'right';
            ctx.fillText(item.value, panelX + panelW - 15, y);
            ctx.textAlign = 'left';
        });
    }

    _drawControlsHint() {
        const ctx = this.ctx;
        const { width, height } = this.options;

        ctx.fillStyle = 'rgba(107, 114, 128, 0.7)';
        ctx.font = '11px system-ui';
        ctx.textAlign = 'center';
        ctx.fillText('🖱️ 拖动旋转 | 滚轮缩放', width / 2, height - 15);
    }

    resize(width, height) {
        this.options.width = width;
        this.options.height = height;
        this.canvas.style.width = width + 'px';
        this.canvas.style.height = height + 'px';
        this.canvas.width = width * this.options.dpr;
        this.canvas.height = height * this.options.dpr;
        this.ctx.scale(this.options.dpr, this.options.dpr);
    }
}

if (typeof module !== 'undefined' && module.exports) {
    module.exports = VirtualReinforcement3D;
}
