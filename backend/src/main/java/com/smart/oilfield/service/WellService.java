package com.smart.oilfield.service;

import com.smart.oilfield.dto.WellDTO;
import com.smart.oilfield.dto.WellTrendDTO;
import com.smart.oilfield.entity.*;
import com.smart.oilfield.repository.*;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Service;

import java.time.LocalDate;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.stream.Collectors;

@Service
public class WellService {

    @Autowired
    private WellRepository wellRepository;

    @Autowired
    private WaterInjectionDataRepository injectionDataRepository;

    @Autowired
    private ProductionDataRepository productionDataRepository;

    public List<WellDTO> getAllWells() {
        return wellRepository.findAll().stream()
                .map(this::convertToDTO)
                .collect(Collectors.toList());
    }

    public List<WellDTO> getWellsByType(String wellType) {
        return wellRepository.findByWellType(wellType).stream()
                .map(this::convertToDTO)
                .collect(Collectors.toList());
    }

    public WellDTO getWellById(String wellId) {
        return wellRepository.findById(wellId)
                .map(this::convertToDTO)
                .orElse(null);
    }

    public WellTrendDTO getWellTrend(String wellId, int days) {
        Well well = wellRepository.findById(wellId).orElse(null);
        if (well == null) {
            return null;
        }

        LocalDate endDate = LocalDate.now();
        LocalDate startDate = endDate.minusDays(days);

        WellTrendDTO trendDTO = new WellTrendDTO();
        trendDTO.setWellId(wellId);
        trendDTO.setWellName(well.getWellName());
        trendDTO.setWellType(well.getWellType());

        List<String> dates = new ArrayList<>();
        List<Double> oilVolumes = new ArrayList<>();
        List<Double> waterVolumes = new ArrayList<>();
        List<Double> waterCuts = new ArrayList<>();
        List<Double> pressures = new ArrayList<>();

        if ("INJECTION".equals(well.getWellType())) {
            List<WaterInjectionData> injectionData = injectionDataRepository
                    .findByWellIdAndReportDateBetweenOrderByReportDate(wellId, startDate, endDate);
            
            for (WaterInjectionData data : injectionData) {
                dates.add(data.getReportDate().toString());
                waterVolumes.add(data.getWaterVolume());
                pressures.add(data.getInjectionPressure());
                waterCuts.add(null);
                oilVolumes.add(null);
            }
        } else {
            List<ProductionData> prodData = productionDataRepository
                    .findByWellIdAndReportDateBetweenOrderByReportDate(wellId, startDate, endDate);
            
            for (ProductionData data : prodData) {
                dates.add(data.getReportDate().toString());
                oilVolumes.add(data.getOilVolume());
                waterCuts.add(data.getWaterCut());
                waterVolumes.add(data.getLiquidVolume() - data.getOilVolume());
                pressures.add(null);
            }
        }

        trendDTO.setDates(dates);
        trendDTO.setOilVolumes(oilVolumes);
        trendDTO.setWaterVolumes(waterVolumes);
        trendDTO.setWaterCuts(waterCuts);
        trendDTO.setPressures(pressures);

        return trendDTO;
    }

    public List<WellDTO> getWellsByBlock(String blockName) {
        return wellRepository.findByBlockName(blockName).stream()
                .map(this::convertToDTO)
                .collect(Collectors.toList());
    }

    private WellDTO convertToDTO(Well well) {
        WellDTO dto = new WellDTO();
        dto.setWellId(well.getWellId());
        dto.setWellName(well.getWellName());
        dto.setWellType(well.getWellType());
        dto.setBlockName(well.getBlockName());
        dto.setDesignPressure(well.getDesignPressure());
        dto.setLongitude(well.getLongitude());
        dto.setLatitude(well.getLatitude());
        dto.setStatus(well.getStatus());

        if ("INJECTION".equals(well.getWellType())) {
            WaterInjectionData latest = injectionDataRepository.findLatestByWellId(well.getWellId());
            if (latest != null) {
                dto.setLatestWaterVolume(latest.getWaterVolume());
                dto.setLatestInjectionPressure(latest.getInjectionPressure());
            }
        } else {
            ProductionData latest = productionDataRepository.findLatestByWellId(well.getWellId());
            if (latest != null) {
                dto.setLatestOilVolume(latest.getOilVolume());
                dto.setLatestWaterCut(latest.getWaterCut());
            }
        }

        return dto;
    }

    public List<String> getAllBlockNames() {
        return wellRepository.findAll().stream()
                .map(Well::getBlockName)
                .distinct()
                .sorted()
                .collect(Collectors.toList());
    }

    public Well saveWell(Well well) {
        return wellRepository.save(well);
    }

    public void deleteWell(String wellId) {
        wellRepository.deleteById(wellId);
    }
}
