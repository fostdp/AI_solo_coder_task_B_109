const API = {
    async getWells(wellType, blockName) {
        const params = new URLSearchParams();
        if (wellType) params.append('wellType', wellType);
        if (blockName && blockName !== 'ALL') params.append('blockName', blockName);
        
        const response = await fetch(`${CONFIG.API_BASE_URL}/wells?${params}`);
        return response.json();
    },

    async getWellById(wellId) {
        const response = await fetch(`${CONFIG.API_BASE_URL}/wells/${wellId}`);
        return response.json();
    },

    async getWellTrend(wellId, days = 90) {
        const response = await fetch(`${CONFIG.API_BASE_URL}/wells/${wellId}/trend?days=${days}`);
        return response.json();
    },

    async getBlocks() {
        const response = await fetch(`${CONFIG.API_BASE_URL}/wells/blocks`);
        return response.json();
    },

    async getCoreIndicators(blockName = 'ALL') {
        const response = await fetch(`${CONFIG.API_BASE_URL}/summary/core-indicators?blockName=${blockName}`);
        return response.json();
    },

    async getRelations(blockName) {
        const params = new URLSearchParams();
        if (blockName && blockName !== 'ALL') params.append('blockName', blockName);
        
        const response = await fetch(`${CONFIG.API_BASE_URL}/relations/map-data?${params}`);
        return response.json();
    },

    async getLatestSuggestions() {
        const response = await fetch(`${CONFIG.API_BASE_URL}/allocation/latest`);
        return response.json();
    },

    async getAlarms() {
        const response = await fetch(`${CONFIG.API_BASE_URL}/alarms/unacknowledged`);
        return response.json();
    },

    async runAllocation() {
        const response = await fetch(`${CONFIG.API_BASE_URL}/allocation/run-now`, {
            method: 'POST'
        });
        return response.json();
    },

    async checkAlarms() {
        const response = await fetch(`${CONFIG.API_BASE_URL}/alarms/check-now`, {
            method: 'POST'
        });
        return response.json();
    },

    async acknowledgeAlarm(alarmId) {
        const response = await fetch(`${CONFIG.API_BASE_URL}/alarms/${alarmId}/acknowledge`, {
            method: 'POST'
        });
        return response.json();
    },

    async getRelationsByWell(wellId, wellType) {
        const endpoint = wellType === 'INJECTION' ? 'injection' : 'production';
        const response = await fetch(`${CONFIG.API_BASE_URL}/relations/${endpoint}/${wellId}`);
        return response.json();
    }
};
