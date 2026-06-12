package com.smart.oilfield.service;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.smart.oilfield.dto.InjectionDataDTO;
import com.smart.oilfield.dto.ProductionDataDTO;
import com.smart.oilfield.entity.WaterInjectionData;
import com.smart.oilfield.entity.ProductionData;
import com.smart.oilfield.repository.WaterInjectionDataRepository;
import com.smart.oilfield.repository.ProductionDataRepository;
import com.smart.oilfield.repository.WellRepository;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Service;

import java.time.LocalDate;

@Slf4j
@Service
public class DataReceiveService {

    @Autowired
    private WaterInjectionDataRepository injectionDataRepository;

    @Autowired
    private ProductionDataRepository productionDataRepository;

    @Autowired
    private WellRepository wellRepository;

    @Autowired
    private ObjectMapper objectMapper;

    public void receiveInjectionData(String payload) {
        try {
            JsonNode node = objectMapper.readTree(payload);
            String wellId = node.get("wellId").asText();

            if (!wellRepository.existsById(wellId)) {
                log.warn("Received data for unknown injection well: {}", wellId);
                return;
            }

            WaterInjectionData data = new WaterInjectionData();
            data.setWellId(wellId);
            data.setReportDate(node.has("reportDate") ? 
                    LocalDate.parse(node.get("reportDate").asText()) : LocalDate.now());
            data.setWaterVolume(node.get("waterVolume").asDouble());
            data.setInjectionPressure(node.get("injectionPressure").asDouble());
            data.setWaterAbsorptionIndex(node.get("waterAbsorptionIndex").asDouble());

            injectionDataRepository.save(data);
            log.info("Received injection data for well: {}, volume: {} m³", wellId, data.getWaterVolume());

        } catch (Exception e) {
            log.error("Failed to parse injection data: {}", payload, e);
        }
    }

    public void receiveProductionData(String payload) {
        try {
            JsonNode node = objectMapper.readTree(payload);
            String wellId = node.get("wellId").asText();

            if (!wellRepository.existsById(wellId)) {
                log.warn("Received data for unknown production well: {}", wellId);
                return;
            }

            ProductionData data = new ProductionData();
            data.setWellId(wellId);
            data.setReportDate(node.has("reportDate") ? 
                    LocalDate.parse(node.get("reportDate").asText()) : LocalDate.now());
            data.setLiquidVolume(node.get("liquidVolume").asDouble());
            data.setOilVolume(node.get("oilVolume").asDouble());
            data.setWaterCut(node.get("waterCut").asDouble());
            data.setDynamicFluidLevel(node.get("dynamicFluidLevel").asDouble());

            productionDataRepository.save(data);
            log.info("Received production data for well: {}, oil: {} t", wellId, data.getOilVolume());

        } catch (Exception e) {
            log.error("Failed to parse production data: {}", payload, e);
        }
    }

    public WaterInjectionData saveInjectionData(InjectionDataDTO dto) {
        if (!wellRepository.existsById(dto.getWellId())) {
            throw new RuntimeException("Well not found: " + dto.getWellId());
        }
        WaterInjectionData data = new WaterInjectionData();
        data.setWellId(dto.getWellId());
        data.setReportDate(dto.getReportDate() != null ? dto.getReportDate() : LocalDate.now());
        data.setWaterVolume(dto.getWaterVolume());
        data.setInjectionPressure(dto.getInjectionPressure());
        data.setWaterAbsorptionIndex(dto.getWaterAbsorptionIndex());
        return injectionDataRepository.save(data);
    }

    public ProductionData saveProductionData(ProductionDataDTO dto) {
        if (!wellRepository.existsById(dto.getWellId())) {
            throw new RuntimeException("Well not found: " + dto.getWellId());
        }
        ProductionData data = new ProductionData();
        data.setWellId(dto.getWellId());
        data.setReportDate(dto.getReportDate() != null ? dto.getReportDate() : LocalDate.now());
        data.setLiquidVolume(dto.getLiquidVolume());
        data.setOilVolume(dto.getOilVolume());
        data.setWaterCut(dto.getWaterCut());
        data.setDynamicFluidLevel(dto.getDynamicFluidLevel());
        return productionDataRepository.save(data);
    }
}
