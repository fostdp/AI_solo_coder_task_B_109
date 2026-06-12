class ChemicalReactionAlert {
    constructor(containerId, options = {}) {
        this.container = document.getElementById(containerId);
        if (!this.container) throw new Error(`Container ${containerId} not found`);

        this.options = {
            width: options.width || 500,
            height: options.height || 350,
            dpr: window.devicePixelRatio || 1,
            ...options
        };

        this.data = null;
        this._render();
    }

    setData(reactionResult) {
        this.data = reactionResult;
        this._render();
    }

    _render() {
        this.container.innerHTML = '';

        const wrapper = document.createElement('div');
        wrapper.style.cssText = `
            width: ${this.options.width}px;
            background: linear-gradient(135deg, #fef3c7 0%, #fde68a 100%);
            border-radius: 12px;
            padding: 20px;
            box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);
            font-family: system-ui, -apple-system, sans-serif;
        `;

        const header = this._createHeader();
        wrapper.appendChild(header);

        if (this.data) {
            const energyDiagram = this._createEnergyDiagram();
            wrapper.appendChild(energyDiagram);

            const metrics = this._createMetricsGrid();
            wrapper.appendChild(metrics);

            const riskBar = this._createRiskIndicator();
            wrapper.appendChild(riskBar);

            const products = this._createProductsList();
            wrapper.appendChild(products);

            const recommendation = this._createRecommendation();
            wrapper.appendChild(recommendation);
        } else {
            const placeholder = document.createElement('div');
            placeholder.style.cssText = `
                text-align: center;
                color: #9ca3af;
                padding: 40px 20px;
            `;
            placeholder.innerHTML = `
                <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                    <path d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z"/>
                    <circle cx="12" cy="12" r="3"/>
                </svg>
                <p style="margin-top: 12px;">请选择材料进行化学反应评估</p>
            `;
            wrapper.appendChild(placeholder);
        }

        this.container.appendChild(wrapper);
    }

    _createHeader() {
        const header = document.createElement('div');
        header.style.cssText = 'display: flex; align-items: center; gap: 12px; margin-bottom: 16px;';

        const icon = document.createElement('div');
        const level = this.data?.WarningLevel || 'NONE';
        const bgColors = {
            'CRITICAL': '#dc2626',
            'WARNING': '#f59e0b',
            'CAUTION': '#0ea5e9',
            'NONE': '#10b981'
        };
        const bgColor = bgColors[level] || '#10b981';

        icon.style.cssText = `
            width: 40px;
            height: 40px;
            border-radius: 10px;
            background: ${bgColor};
            display: flex;
            align-items: center;
            justify-content: center;
            color: white;
        `;

        const levelIcons = {
            'CRITICAL': '⚠️',
            'WARNING': '⚡',
            'CAUTION': '💧',
            'NONE': '✓'
        };
        icon.textContent = levelIcons[level] || '✓';

        const titleBox = document.createElement('div');
        titleBox.innerHTML = `
            <div style="font-size: 16px; font-weight: 600; color: #1f2937;">
                ${this.data?.ReactionName || '化学反应评估'}
            </div>
            <div style="font-size: 13px; color: #6b7280; margin-top: 2px;">
                ${this.data?.MaterialName || ''}
            </div>
        `;

        header.appendChild(icon);
        header.appendChild(titleBox);

        return header;
    }

    _createEnergyDiagram() {
        const diagram = document.createElement('div');
        diagram.style.cssText = `
            background: white;
            border-radius: 8px;
            padding: 15px;
            margin-bottom: 16px;
        `;

        const title = document.createElement('div');
        title.style.cssText = 'font-size: 13px; font-weight: 500; color: #374151; margin-bottom: 12px;';
        title.textContent = '吉布斯自由能曲线 (ΔG)';
        diagram.appendChild(title);

        const canvas = document.createElement('canvas');
        const width = this.options.width - 70;
        const height = 100;
        canvas.width = width * this.options.dpr;
        canvas.height = height * this.options.dpr;
        canvas.style.width = width + 'px';
        canvas.style.height = height + 'px';
        const ctx = canvas.getContext('2d');
        ctx.scale(this.options.dpr, this.options.dpr);

        const deltaG = this.data?.GibbsFreeEnergyKJmol || 0;
        const isSpontaneous = this.data?.IsSpontaneous || false;

        ctx.fillStyle = '#f9fafb';
        ctx.fillRect(0, 0, width, height);

        ctx.strokeStyle = '#e5e7eb';
        ctx.lineWidth = 1;
        for (let i = 0; i <= 4; i++) {
            const y = (i / 4) * height;
            ctx.beginPath();
            ctx.moveTo(0, y);
            ctx.lineTo(width, y);
            ctx.stroke();
        }

        const maxAbsG = Math.max(Math.abs(deltaG), 50);
        const yZero = height / 2;
        const scale = yZero / maxAbsG;
        const yG = yZero - deltaG * scale;

        const gradient = ctx.createLinearGradient(0, 0, 0, height);
        if (deltaG < 0) {
            gradient.addColorStop(0, '#10b98130');
            gradient.addColorStop(1, '#10b98110');
        } else {
            gradient.addColorStop(0, '#ef444410');
            gradient.addColorStop(1, '#ef444430');
        }

        ctx.fillStyle = gradient;
        ctx.beginPath();
        ctx.moveTo(0, yZero);
        for (let i = 0; i <= 20; i++) {
            const x = (i / 20) * width;
            const t = i / 20;
            const activationBarrier = 60 * Math.sin(t * Math.PI);
            const y = yZero - (deltaG * t + activationBarrier) * scale;
            ctx.lineTo(x, Math.max(5, Math.min(height - 5, y)));
        }
        ctx.lineTo(width, yG);
        ctx.lineTo(width, yZero);
        ctx.closePath();
        ctx.fill();

        ctx.strokeStyle = isSpontaneous ? '#10b981' : '#ef4444';
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(0, yZero);
        for (let i = 0; i <= 20; i++) {
            const x = (i / 20) * width;
            const t = i / 20;
            const activationBarrier = 60 * Math.sin(t * Math.PI);
            const y = yZero - (deltaG * t + activationBarrier) * scale;
            ctx.lineTo(x, Math.max(5, Math.min(height - 5, y)));
        }
        ctx.stroke();

        ctx.fillStyle = '#374151';
        ctx.font = '10px system-ui';
        ctx.textAlign = 'left';
        ctx.fillText('反应物', 5, yZero - 5);
        ctx.textAlign = 'right';
        ctx.fillText('生成物', width - 5, yG + (yG < yZero ? -5 : 15));

        ctx.fillStyle = isSpontaneous ? '#10b981' : '#ef4444';
        ctx.font = 'bold 12px system-ui';
        ctx.textAlign = 'center';
        ctx.fillText(`ΔG = ${deltaG.toFixed(1)} kJ/mol`, width / 2, 18);
        ctx.fillText(isSpontaneous ? '自发反应 ✓' : '非自发反应 ✗', width / 2, 35);

        diagram.appendChild(canvas);
        return diagram;
    }

    _createMetricsGrid() {
        const grid = document.createElement('div');
        grid.style.cssText = `
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 10px;
            margin-bottom: 16px;
        `;

        const metrics = [
            { label: '平衡常数', value: this.data?.EquilibriumConstant?.toExponential(3) || 'N/A', color: '#0ea5e9' },
            { label: '反应速率', value: this.data?.ReactionRateMolLs?.toExponential(2) + ' mol/L·s' || 'N/A', color: '#8b5cf6' },
            { label: '转化率', value: this.data?.ConversionRate?.toFixed(1) + '%' || 'N/A', color: '#f59e0b' },
            { label: '半衰期', value: this.data?.HalfLifeHours?.toFixed(1) + ' h' || 'N/A', color: '#ec4899' }
        ];

        metrics.forEach(m => {
            const item = document.createElement('div');
            item.style.cssText = `
                background: white;
                border-radius: 8px;
                padding: 12px;
                text-align: center;
            `;
            item.innerHTML = `
                <div style="font-size: 11px; color: #6b7280; margin-bottom: 4px;">${m.label}</div>
                <div style="font-size: 14px; font-weight: 600; color: ${m.color};">${m.value}</div>
            `;
            grid.appendChild(item);
        });

        return grid;
    }

    _createRiskIndicator() {
        const wrapper = document.createElement('div');
        wrapper.style.cssText = 'margin-bottom: 16px;';

        const label = document.createElement('div');
        label.style.cssText = 'font-size: 13px; font-weight: 500; color: #374151; margin-bottom: 8px;';
        label.innerHTML = `有害产物生成风险 <span style="float: right;">${(this.data?.HarmfulProductYield * 100 || 0).toFixed(1)}%</span>`;
        wrapper.appendChild(label);

        const barContainer = document.createElement('div');
        barContainer.style.cssText = `
            height: 12px;
            background: white;
            border-radius: 6px;
            overflow: hidden;
            position: relative;
        `;

        const bar = document.createElement('div');
        const yield_ = this.data?.HarmfulProductYield || 0;
        const width = Math.min(yield_ * 200, 100);
        const color = yield_ > 0.3 ? '#dc2626' : yield_ > 0.15 ? '#f59e0b' : '#10b981';

        bar.style.cssText = `
            height: 100%;
            width: ${width}%;
            background: linear-gradient(90deg, ${color}cc, ${color});
            transition: width 0.5s ease;
        `;
        barContainer.appendChild(bar);

        const thresholdMarkers = [0.075, 0.15, 0.3];
        thresholdMarkers.forEach(t => {
            const marker = document.createElement('div');
            marker.style.cssText = `
                position: absolute;
                top: 0;
                left: ${t * 200}%;
                width: 2px;
                height: 100%;
                background: rgba(0,0,0,0.3);
            `;
            barContainer.appendChild(marker);
        });

        wrapper.appendChild(barContainer);

        const labels = document.createElement('div');
        labels.style.cssText = 'display: flex; justify-content: space-between; font-size: 10px; color: #9ca3af; margin-top: 4px;';
        labels.innerHTML = '<span>安全</span><span>注意</span><span>警告</span><span>危险</span>';
        wrapper.appendChild(labels);

        return wrapper;
    }

    _createProductsList() {
        const wrapper = document.createElement('div');
        wrapper.style.cssText = `
            background: white;
            border-radius: 8px;
            padding: 12px;
            margin-bottom: 16px;
        `;

        const title = document.createElement('div');
        title.style.cssText = 'font-size: 13px; font-weight: 500; color: #374151; margin-bottom: 8px;';
        title.textContent = '有害产物';
        wrapper.appendChild(title);

        const products = this.data?.HarmfulProducts || [];
        if (products.length === 0) {
            const none = document.createElement('div');
            none.style.cssText = 'font-size: 12px; color: #10b981;';
            none.textContent = '✓ 无已知有害产物';
            wrapper.appendChild(none);
        } else {
            const list = document.createElement('div');
            list.style.cssText = 'display: flex; flex-wrap: wrap; gap: 6px;';
            products.forEach(p => {
                const tag = document.createElement('span');
                tag.style.cssText = `
                    padding: 4px 10px;
                    background: #fef2f2;
                    color: #dc2626;
                    border-radius: 12px;
                    font-size: 11px;
                    font-weight: 500;
                `;
                tag.textContent = p;
                list.appendChild(tag);
            });
            wrapper.appendChild(list);
        }

        return wrapper;
    }

    _createRecommendation() {
        const wrapper = document.createElement('div');
        const level = this.data?.WarningLevel || 'NONE';
        const bgColors = {
            'CRITICAL': '#fef2f2',
            'WARNING': '#fffbeb',
            'CAUTION': '#eff6ff',
            'NONE': '#ecfdf5'
        };
        const textColors = {
            'CRITICAL': '#dc2626',
            'WARNING': '#b45309',
            'CAUTION': '#1d4ed8',
            'NONE': '#047857'
        };

        wrapper.style.cssText = `
            background: ${bgColors[level]};
            border-left: 4px solid ${textColors[level]};
            border-radius: 6px;
            padding: 12px 16px;
        `;

        wrapper.innerHTML = `
            <div style="font-size: 13px; font-weight: 600; color: ${textColors[level]}; margin-bottom: 4px;">
                评估建议
            </div>
            <div style="font-size: 12px; color: #374151; line-height: 1.5;">
                ${this.data?.Recommendation || '请进行实验室兼容性测试'}
            </div>
        `;

        return wrapper;
    }
}

if (typeof module !== 'undefined' && module.exports) {
    module.exports = ChemicalReactionAlert;
}
