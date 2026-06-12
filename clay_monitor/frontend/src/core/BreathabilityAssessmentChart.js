class BreathabilityAssessmentChart {
    constructor(containerId, options = {}) {
        this.container = document.getElementById(containerId);
        if (!this.container) throw new Error(`Container ${containerId} not found`);

        this.options = {
            width: options.width || 650,
            height: options.height || 450,
            dpr: window.devicePixelRatio || 1,
            ...options
        };

        this.data = null;
        this._render();
    }

    setData(result) {
        this.data = result;
        this._render();
    }

    _render() {
        this.container.innerHTML = '';

        const wrapper = document.createElement('div');
        wrapper.style.cssText = `
            width: ${this.options.width}px;
            font-family: system-ui, -apple-system, sans-serif;
        `;

        const header = this._createHeader();
        wrapper.appendChild(header);

        const mainGrid = document.createElement('div');
        mainGrid.style.cssText = 'display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin-top: 16px;';

        if (this.data && this.data.Temperatures && this.data.Temperatures.length >= 10) {
            const timeseriesChart = this._createTimeseriesChart();
            mainGrid.appendChild(timeseriesChart);

            const hysteresisChart = this._createHysteresisChart();
            mainGrid.appendChild(hysteresisChart);
        } else {
            const placeholder = this._createPlaceholder();
            wrapper.appendChild(placeholder);
        }

        wrapper.appendChild(mainGrid);

        if (this.data) {
            const metricsGrid = this._createMetricsGrid();
            wrapper.appendChild(metricsGrid);

            const scoreCard = this._createScoreCard();
            wrapper.appendChild(scoreCard);

            const recommendation = this._createRecommendation();
            wrapper.appendChild(recommendation);
        }

        this.container.appendChild(wrapper);
    }

    _createHeader() {
        const header = document.createElement('div');
        header.style.cssText = 'display: flex; align-items: center; justify-content: space-between;';

        const title = document.createElement('div');
        title.innerHTML = `
            <div style="font-size: 18px; font-weight: 600; color: #1f2937;">泥塑呼吸性评估</div>
            <div style="font-size: 13px; color: #6b7280; margin-top: 4px;">基于温湿度波动的自调节能力分析</div>
        `;

        const statusBadge = document.createElement('div');
        const level = this.data?.RegulationLevel || 'UNKNOWN';
        const badgeColors = {
            'EXCELLENT': { bg: '#d1fae5', text: '#065f46' },
            'GOOD': { bg: '#dbeafe', text: '#1e40af' },
            'FAIR': { bg: '#fef3c7', text: '#92400e' },
            'POOR': { bg: '#fee2e2', text: '#991b1b' },
            'CRITICAL': { bg: '#fecaca', text: '#7f1d1d' }
        };
        const colors = badgeColors[level] || { bg: '#f3f4f6', text: '#374151' };

        statusBadge.style.cssText = `
            padding: 8px 16px;
            background: ${colors.bg};
            color: ${colors.text};
            border-radius: 20px;
            font-size: 13px;
            font-weight: 600;
        `;
        statusBadge.textContent = this._getLevelText(level);

        header.appendChild(title);
        header.appendChild(statusBadge);

        return header;
    }

    _createTimeseriesChart() {
        const container = document.createElement('div');
        container.style.cssText = 'background: #fafafa; border-radius: 12px; padding: 16px;';

        const title = document.createElement('div');
        title.style.cssText = 'font-size: 14px; font-weight: 500; color: #374151; margin-bottom: 12px;';
        title.textContent = '温湿度时序曲线';
        container.appendChild(title);

        const canvas = document.createElement('canvas');
        const width = (this.options.width - 16) / 2 - 32;
        const height = 180;
        canvas.width = width * this.options.dpr;
        canvas.height = height * this.options.dpr;
        canvas.style.width = width + 'px';
        canvas.style.height = height + 'px';
        const ctx = canvas.getContext('2d');
        ctx.scale(this.options.dpr, this.options.dpr);

        const T = this.data.Temperatures;
        const RH = this.data.Humidities;
        const n = T.length;

        const margin = { top: 20, right: 40, bottom: 30, left: 50 };
        const plotW = width - margin.left - margin.right;
        const plotH = height - margin.top - margin.bottom;

        const tMin = Math.min(...T) - 2;
        const tMax = Math.max(...T) + 2;
        const rhMin = Math.min(...RH) - 5;
        const rhMax = Math.max(...RH) + 5;

        ctx.strokeStyle = '#e5e7eb';
        ctx.lineWidth = 1;
        for (let i = 0; i <= 4; i++) {
            const y = margin.top + (i / 4) * plotH;
            ctx.beginPath();
            ctx.moveTo(margin.left, y);
            ctx.lineTo(margin.left + plotW, y);
            ctx.stroke();
        }

        ctx.strokeStyle = '#ef4444';
        ctx.lineWidth = 2;
        ctx.beginPath();
        for (let i = 0; i < n; i++) {
            const x = margin.left + (i / (n - 1)) * plotW;
            const y = margin.top + plotH - ((T[i] - tMin) / (tMax - tMin)) * plotH;
            if (i === 0) ctx.moveTo(x, y);
            else ctx.lineTo(x, y);
        }
        ctx.stroke();

        ctx.strokeStyle = '#3b82f6';
        ctx.lineWidth = 2;
        ctx.beginPath();
        for (let i = 0; i < n; i++) {
            const x = margin.left + (i / (n - 1)) * plotW;
            const y = margin.top + plotH - ((RH[i] - rhMin) / (rhMax - rhMin)) * plotH;
            if (i === 0) ctx.moveTo(x, y);
            else ctx.lineTo(x, y);
        }
        ctx.stroke();

        ctx.fillStyle = '#6b7280';
        ctx.font = '10px system-ui';
        ctx.textAlign = 'right';
        for (let i = 0; i <= 4; i++) {
            const y = margin.top + (i / 4) * plotH;
            const tVal = tMax - (i / 4) * (tMax - tMin);
            ctx.fillText(tVal.toFixed(1) + '℃', margin.left - 5, y + 3);
        }

        ctx.fillStyle = '#3b82f6';
        ctx.textAlign = 'left';
        for (let i = 0; i <= 4; i++) {
            const y = margin.top + (i / 4) * plotH;
            const rhVal = rhMax - (i / 4) * (rhMax - rhMin);
            ctx.fillText(rhVal.toFixed(0) + '%', margin.left + plotW + 5, y + 3);
        }

        const legend = document.createElement('div');
        legend.style.cssText = 'display: flex; gap: 16px; justify-content: center; margin-top: 8px;';
        legend.innerHTML = `
            <span style="display: flex; align-items: center; gap: 6px; font-size: 11px; color: #6b7280;">
                <span style="width: 12px; height: 2px; background: #ef4444;"></span> 温度
            </span>
            <span style="display: flex; align-items: center; gap: 6px; font-size: 11px; color: #6b7280;">
                <span style="width: 12px; height: 2px; background: #3b82f6;"></span> 湿度
            </span>
        `;

        container.appendChild(canvas);
        container.appendChild(legend);

        return container;
    }

    _createHysteresisChart() {
        const container = document.createElement('div');
        container.style.cssText = 'background: #fafafa; border-radius: 12px; padding: 16px;';

        const title = document.createElement('div');
        title.style.cssText = 'font-size: 14px; font-weight: 500; color: #374151; margin-bottom: 12px;';
        title.textContent = '吸湿-放湿回滞曲线';
        container.appendChild(title);

        const canvas = document.createElement('canvas');
        const width = (this.options.width - 16) / 2 - 32;
        const height = 180;
        canvas.width = width * this.options.dpr;
        canvas.height = height * this.options.dpr;
        canvas.style.width = width + 'px';
        canvas.style.height = height + 'px';
        const ctx = canvas.getContext('2d');
        ctx.scale(this.options.dpr, this.options.dpr);

        const sorption = this.data.MoistureSorptionCurve || [];
        const desorption = this.data.MoistureDesorptionCurve || [];
        const n = Math.max(sorption.length, desorption.length, 1);

        const margin = { top: 20, right: 20, bottom: 35, left: 50 };
        const plotW = width - margin.left - margin.right;
        const plotH = height - margin.top - margin.bottom;

        ctx.strokeStyle = '#e5e7eb';
        ctx.lineWidth = 1;
        for (let i = 0; i <= 4; i++) {
            const y = margin.top + (i / 4) * plotH;
            ctx.beginPath();
            ctx.moveTo(margin.left, y);
            ctx.lineTo(margin.left + plotW, y);
            ctx.stroke();

            const x = margin.left + (i / 4) * plotW;
            ctx.beginPath();
            ctx.moveTo(x, margin.top);
            ctx.lineTo(x, margin.top + plotH);
            ctx.stroke();
        }

        const maxMoisture = Math.max(...sorption, ...desorption, 0.1);

        ctx.fillStyle = '#10b98120';
        ctx.strokeStyle = '#10b981';
        ctx.lineWidth = 2;
        ctx.beginPath();
        sorption.forEach((m, i) => {
            const x = margin.left + (i / (n - 1)) * plotW;
            const y = margin.top + plotH - (m / maxMoisture) * plotH;
            if (i === 0) ctx.moveTo(x, y);
            else ctx.lineTo(x, y);
        });
        ctx.stroke();

        ctx.fillStyle = '#f59e0b20';
        ctx.strokeStyle = '#f59e0b';
        ctx.lineWidth = 2;
        ctx.beginPath();
        desorption.forEach((m, i) => {
            const x = margin.left + (i / (n - 1)) * plotW;
            const y = margin.top + plotH - (m / maxMoisture) * plotH;
            if (i === 0) ctx.moveTo(x, y);
            else ctx.lineTo(x, y);
        });
        ctx.stroke();

        ctx.fillStyle = '#10b981';
        sorption.forEach((m, i) => {
            const x = margin.left + (i / (n - 1)) * plotW;
            const y = margin.top + plotH - (m / maxMoisture) * plotH;
            ctx.beginPath();
            ctx.arc(x, y, 3, 0, Math.PI * 2);
            ctx.fill();
        });

        ctx.fillStyle = '#f59e0b';
        desorption.forEach((m, i) => {
            const x = margin.left + (i / (n - 1)) * plotW;
            const y = margin.top + plotH - (m / maxMoisture) * plotH;
            ctx.beginPath();
            ctx.arc(x, y, 3, 0, Math.PI * 2);
            ctx.fill();
        });

        ctx.fillStyle = '#6b7280';
        ctx.font = '10px system-ui';
        ctx.textAlign = 'right';
        for (let i = 0; i <= 4; i++) {
            const y = margin.top + (i / 4) * plotH;
            const val = maxMoisture - (i / 4) * maxMoisture;
            ctx.fillText(val.toFixed(3), margin.left - 5, y + 3);
        }

        ctx.textAlign = 'center';
        for (let i = 0; i <= 4; i++) {
            const x = margin.left + (i / 4) * plotW;
            ctx.fillText((i * 25) + '%', x, margin.top + plotH + 15);
        }

        ctx.fillStyle = '#6b7280';
        ctx.font = '11px system-ui';
        ctx.textAlign = 'center';
        ctx.fillText('相对湿度 RH%', margin.left + plotW / 2, height - 5);

        ctx.save();
        ctx.translate(15, margin.top + plotH / 2);
        ctx.rotate(-Math.PI / 2);
        ctx.fillText('含水率', 0, 0);
        ctx.restore();

        const legend = document.createElement('div');
        legend.style.cssText = 'display: flex; gap: 16px; justify-content: center; margin-top: 8px;';
        legend.innerHTML = `
            <span style="display: flex; align-items: center; gap: 6px; font-size: 11px; color: #6b7280;">
                <span style="width: 12px; height: 2px; background: #10b981;"></span> 吸湿
            </span>
            <span style="display: flex; align-items: center; gap: 6px; font-size: 11px; color: #6b7280;">
                <span style="width: 12px; height: 2px; background: #f59e0b;"></span> 放湿
            </span>
            <span style="display: flex; align-items: center; gap: 6px; font-size: 11px; color: #6b7280;">
                回滞面积: ${(this.data?.HysteresisArea || 0).toFixed(4)}
            </span>
        `;

        container.appendChild(canvas);
        container.appendChild(legend);

        return container;
    }

    _createMetricsGrid() {
        const grid = document.createElement('div');
        grid.style.cssText = `
            display: grid;
            grid-template-columns: repeat(4, 1fr);
            gap: 12px;
            margin-top: 16px;
        `;

        const metrics = [
            {
                label: '呼吸频率',
                value: `${(this.data?.BreathFrequencyCyclesPerDay || 0).toFixed(2)} 次/天`,
                icon: '💨',
                color: '#0ea5e9'
            },
            {
                label: '呼吸强度',
                value: (this.data?.BreathIntensity || 0).toFixed(4),
                icon: '📊',
                color: '#8b5cf6'
            },
            {
                label: '响应时滞',
                value: `${(this.data?.TimeLagMinutes || 0).toFixed(1)} 分钟`,
                icon: '⏱️',
                color: '#f59e0b'
            },
            {
                label: '缓冲容量',
                value: (this.data?.MoistureBufferCapacity || 0).toFixed(2),
                icon: '💧',
                color: '#ec4899'
            },
            {
                label: '吸湿循环',
                value: `${this.data?.AbsorptionCycles || 0} 次`,
                icon: '⬆️',
                color: '#10b981'
            },
            {
                label: '放湿循环',
                value: `${this.data?.DesorptionCycles || 0} 次`,
                icon: '⬇️',
                color: '#ef4444'
            },
            {
                label: '平均周期',
                value: `${(this.data?.AverageCycleDurationMinutes || 0).toFixed(0)} 分钟`,
                icon: '🔄',
                color: '#6366f1'
            },
            {
                label: '温度波动',
                value: `${(this.data?.TemperatureAmplitudeC || 0).toFixed(1)} ℃`,
                icon: '🌡️',
                color: '#ef4444'
            }
        ];

        metrics.forEach(m => {
            const card = document.createElement('div');
            card.style.cssText = `
                background: white;
                border: 1px solid #e5e7eb;
                border-radius: 10px;
                padding: 14px;
                text-align: center;
            `;
            card.innerHTML = `
                <div style="font-size: 20px; margin-bottom: 4px;">${m.icon}</div>
                <div style="font-size: 11px; color: #6b7280; margin-bottom: 4px;">${m.label}</div>
                <div style="font-size: 15px; font-weight: 600; color: ${m.color};">${m.value}</div>
            `;
            grid.appendChild(card);
        });

        return grid;
    }

    _createScoreCard() {
        const score = this.data?.SelfRegulationScore || 0;

        const card = document.createElement('div');
        card.style.cssText = `
            background: linear-gradient(135deg, #1e40af 0%, #3b82f6 100%);
            border-radius: 12px;
            padding: 20px;
            margin-top: 16px;
            color: white;
            position: relative;
            overflow: hidden;
        `;

        const canvas = document.createElement('canvas');
        const size = 120;
        canvas.width = size * this.options.dpr;
        canvas.height = size * this.options.dpr;
        canvas.style.cssText = `
            position: absolute;
            right: 20px;
            top: 50%;
            transform: translateY(-50%);
            width: ${size}px;
            height: ${size}px;
        `;
        const ctx = canvas.getContext('2d');
        ctx.scale(this.options.dpr, this.options.dpr);

        ctx.strokeStyle = 'rgba(255,255,255,0.2)';
        ctx.lineWidth = 12;
        ctx.beginPath();
        ctx.arc(size / 2, size / 2, size / 2 - 10, 0, Math.PI * 2);
        ctx.stroke();

        const angle = (score / 100) * Math.PI * 2;
        const gradient = ctx.createLinearGradient(0, 0, size, size);
        gradient.addColorStop(0, '#34d399');
        gradient.addColorStop(1, '#10b981');
        ctx.strokeStyle = gradient;
        ctx.lineWidth = 12;
        ctx.lineCap = 'round';
        ctx.beginPath();
        ctx.arc(size / 2, size / 2, size / 2 - 10, -Math.PI / 2, -Math.PI / 2 + angle);
        ctx.stroke();

        ctx.fillStyle = 'white';
        ctx.font = 'bold 28px system-ui';
        ctx.textAlign = 'center';
        ctx.fillText(score.toFixed(0), size / 2, size / 2 + 10);
        ctx.font = '10px system-ui';
        ctx.fillText('综合评分', size / 2, size / 2 + 28);

        card.innerHTML = `
            <div style="font-size: 14px; opacity: 0.9;">自调节能力综合评分</div>
            <div style="font-size: 36px; font-weight: 700; margin: 8px 0;">${score.toFixed(1)}<span style="font-size: 18px; opacity: 0.7;">/100</span></div>
            <div style="font-size: 13px; opacity: 0.85; max-width: 60%; line-height: 1.5;">
                ${this.data?.Assessment || ''}
            </div>
        `;
        card.appendChild(canvas);

        return card;
    }

    _createRecommendation() {
        const wrapper = document.createElement('div');
        const level = this.data?.RegulationLevel || 'NONE';

        const styles = {
            'EXCELLENT': { bg: '#d1fae5', border: '#10b981', text: '#065f46' },
            'GOOD': { bg: '#dbeafe', border: '#3b82f6', text: '#1e40af' },
            'FAIR': { bg: '#fef3c7', border: '#f59e0b', text: '#92400e' },
            'POOR': { bg: '#fee2e2', border: '#ef4444', text: '#991b1b' },
            'CRITICAL': { bg: '#fecaca', border: '#dc2626', text: '#7f1d1d' }
        };
        const s = styles[level] || { bg: '#f3f4f6', border: '#6b7280', text: '#374151' };

        wrapper.style.cssText = `
            margin-top: 16px;
            padding: 16px;
            background: ${s.bg};
            border-left: 4px solid ${s.border};
            border-radius: 8px;
        `;

        wrapper.innerHTML = `
            <div style="font-size: 14px; font-weight: 600; color: ${s.text}; margin-bottom: 6px;">
                📋 保护建议
            </div>
            <div style="font-size: 13px; color: #374151; line-height: 1.6;">
                ${this.data?.Recommendation || '定期监测泥塑呼吸状态'}
            </div>
        `;

        return wrapper;
    }

    _createPlaceholder() {
        const placeholder = document.createElement('div');
        placeholder.style.cssText = `
            grid-column: span 2;
            background: #fafafa;
            border-radius: 12px;
            padding: 40px;
            text-align: center;
            color: #9ca3af;
        `;
        placeholder.innerHTML = `
            <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" style="margin: 0 auto 12px;">
                <path d="M12 6v12m-3-2.818l.879.659c1.171.879 3.07.879 4.242 0 1.172-.879 1.172-2.303 0-3.182C13.536 12.219 12.768 12 12 12c-.725 0-1.45-.22-2.003-.659-1.106-.879-1.106-2.303 0-3.182s2.9-.879 4.006 0l.415.33M21 12a9 9 0 11-18 0 9 9 0 0118 0z"/>
            </svg>
            <p style="margin: 0; font-size: 14px;">需要至少10个温湿度数据点进行呼吸性分析</p>
            <p style="margin: 8px 0 0; font-size: 12px;">请确保传感器已正常工作足够时间</p>
        `;
        return placeholder;
    }

    _getLevelText(level) {
        const texts = {
            'EXCELLENT': '🌟 优秀',
            'GOOD': '✅ 良好',
            'FAIR': '⚠️ 一般',
            'POOR': '❗ 较差',
            'CRITICAL': '🚨 危险'
        };
        return texts[level] || '待评估';
    }
}

if (typeof module !== 'undefined' && module.exports) {
    module.exports = BreathabilityAssessmentChart;
}
