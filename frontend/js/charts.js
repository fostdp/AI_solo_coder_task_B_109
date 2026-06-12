const ChartManager = {
    charts: {},

    createProductionTrend(canvasId, data) {
        const ctx = document.getElementById(canvasId);
        if (!ctx) return null;

        if (this.charts[canvasId]) {
            this.charts[canvasId].destroy();
        }

        const datasets = [];

        if (data.oilVolumes && data.oilVolumes.some(v => v !== null)) {
            datasets.push({
                label: '产油量 (t)',
                data: data.oilVolumes,
                borderColor: '#ff9800',
                backgroundColor: 'rgba(255, 152, 0, 0.1)',
                fill: true,
                tension: 0.4,
                yAxisID: 'y'
            });
        }

        if (data.waterVolumes && data.waterVolumes.some(v => v !== null)) {
            datasets.push({
                label: data.wellType === 'INJECTION' ? '注水量 (m³)' : '产水量 (m³)',
                data: data.waterVolumes,
                borderColor: '#2196f3',
                backgroundColor: 'rgba(33, 150, 243, 0.1)',
                fill: true,
                tension: 0.4,
                yAxisID: 'y'
            });
        }

        if (data.waterCuts && data.waterCuts.some(v => v !== null)) {
            datasets.push({
                label: '含水率 (%)',
                data: data.waterCuts,
                borderColor: '#e91e63',
                backgroundColor: 'transparent',
                borderDash: [5, 5],
                tension: 0.4,
                yAxisID: 'y1'
            });
        }

        if (data.pressures && data.pressures.some(v => v !== null)) {
            datasets.push({
                label: '注水压力 (MPa)',
                data: data.pressures,
                borderColor: '#9c27b0',
                backgroundColor: 'transparent',
                borderDash: [2, 2],
                tension: 0.4,
                yAxisID: 'y2'
            });
        }

        const yAxes = {
            y: {
                type: 'linear',
                display: true,
                position: 'left',
                grid: {
                    color: 'rgba(255, 255, 255, 0.1)'
                },
                ticks: {
                    color: '#90a4ae'
                }
            }
        };

        if (data.waterCuts && data.waterCuts.some(v => v !== null)) {
            yAxes.y1 = {
                type: 'linear',
                display: true,
                position: 'right',
                min: 0,
                max: 100,
                grid: {
                    drawOnChartArea: false
                },
                ticks: {
                    color: '#e91e63',
                    callback: value => value + '%'
                }
            };
        }

        if (data.pressures && data.pressures.some(v => v !== null)) {
            yAxes.y2 = {
                type: 'linear',
                display: true,
                position: 'right',
                grid: {
                    drawOnChartArea: false
                },
                ticks: {
                    color: '#9c27b0'
                }
            };
        }

        this.charts[canvasId] = new Chart(ctx, {
            type: 'line',
            data: {
                labels: data.dates,
                datasets: datasets
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                interaction: {
                    mode: 'index',
                    intersect: false
                },
                plugins: {
                    legend: {
                        display: true,
                        position: 'top',
                        labels: {
                            color: '#e0e6ed',
                            font: {
                                size: 11
                            },
                            boxWidth: 12
                        }
                    },
                    tooltip: {
                        backgroundColor: 'rgba(13, 33, 55, 0.95)',
                        borderColor: '#2d5a87',
                        borderWidth: 1,
                        titleColor: '#4fc3f7',
                        bodyColor: '#e0e6ed',
                        padding: 12,
                        displayColors: true
                    }
                },
                scales: {
                    x: {
                        grid: {
                            color: 'rgba(255, 255, 255, 0.05)'
                        },
                        ticks: {
                            color: '#90a4ae',
                            maxTicksLimit: 8,
                            maxRotation: 45,
                            minRotation: 45
                        }
                    },
                    ...yAxes
                }
            }
        });

        return this.charts[canvasId];
    },

    createInjectionProductionAnalysis(canvasId, injectionData, productionData) {
        const ctx = document.getElementById(canvasId);
        if (!ctx) return null;

        if (this.charts[canvasId]) {
            this.charts[canvasId].destroy();
        }

        const labels = injectionData.dates || [];
        const injectionVolumes = injectionData.waterVolumes || [];
        const productionVolumes = productionData.oilVolumes || [];
        const waterCuts = productionData.waterCuts || [];

        this.charts[canvasId] = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [
                    {
                        label: '日注水量 (m³)',
                        data: injectionVolumes,
                        backgroundColor: 'rgba(33, 150, 243, 0.6)',
                        borderColor: '#2196f3',
                        borderWidth: 1,
                        yAxisID: 'y'
                    },
                    {
                        label: '日产油量 (t)',
                        data: productionVolumes,
                        backgroundColor: 'rgba(255, 152, 0, 0.6)',
                        borderColor: '#ff9800',
                        borderWidth: 1,
                        yAxisID: 'y'
                    },
                    {
                        label: '含水率 (%)',
                        data: waterCuts,
                        type: 'line',
                        borderColor: '#e91e63',
                        backgroundColor: 'transparent',
                        borderDash: [5, 5],
                        tension: 0.4,
                        yAxisID: 'y1'
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        display: true,
                        position: 'top',
                        labels: {
                            color: '#e0e6ed',
                            font: {
                                size: 11
                            },
                            boxWidth: 12
                        }
                    },
                    tooltip: {
                        backgroundColor: 'rgba(13, 33, 55, 0.95)',
                        borderColor: '#2d5a87',
                        borderWidth: 1,
                        titleColor: '#4fc3f7',
                        bodyColor: '#e0e6ed',
                        padding: 12
                    }
                },
                scales: {
                    x: {
                        grid: {
                            color: 'rgba(255, 255, 255, 0.05)'
                        },
                        ticks: {
                            color: '#90a4ae',
                            maxTicksLimit: 8,
                            maxRotation: 45,
                            minRotation: 45
                        }
                    },
                    y: {
                        type: 'linear',
                        display: true,
                        position: 'left',
                        grid: {
                            color: 'rgba(255, 255, 255, 0.1)'
                        },
                        ticks: {
                            color: '#90a4ae'
                        }
                    },
                    y1: {
                        type: 'linear',
                        display: true,
                        position: 'right',
                        min: 0,
                        max: 100,
                        grid: {
                            drawOnChartArea: false
                        },
                        ticks: {
                            color: '#e91e63',
                            callback: value => value + '%'
                        }
                    }
                }
            }
        });

        return this.charts[canvasId];
    },

    destroy(canvasId) {
        if (this.charts[canvasId]) {
            this.charts[canvasId].destroy();
            delete this.charts[canvasId];
        }
    },

    destroyAll() {
        for (const key in this.charts) {
            this.charts[key].destroy();
        }
        this.charts = {};
    }
};
