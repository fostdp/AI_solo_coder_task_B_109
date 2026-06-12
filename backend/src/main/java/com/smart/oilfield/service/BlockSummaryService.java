package com.smart.oilfield.service;

import com.smart.oilfield.dto.BlockSummaryDTO;
import com.smart.oilfield.entity.BlockDailySummary;
import com.smart.oilfield.repository.BlockDailySummaryRepository;
import com.smart.oilfield.repository.ProductionDataRepository;
import com.smart.oilfield.repository.WaterInjectionDataRepository;
import com.smart.oilfield.repository.WellRepository;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Service;

import java.time.LocalDate;
import java.util.List;
import java.util.stream.Collectors;

@Slf4j
@Service
public class BlockSummaryService {

    @Autowired
    private BlockDailySummaryRepository summaryRepository;

    @Autowired
    private ProductionDataRepository productionDataRepository;

    @Autowired
    private WaterInjectionDataRepository injectionDataRepository;

    @Autowired
    private WellRepository wellRepository;

    @Scheduled(cron = "0 30 0 * * ?")
    public void scheduledDailySummary() {
        log.info("Starting daily block summary calculation...");
        LocalDate yesterday = LocalDate.now().minusDays(1);
        generateDailySummary(yesterday);
        log.info("Daily block summary calculation completed");
    }

    public void generateDailySummary(LocalDate date) {
        List<String> blocks = wellRepository.findAll().stream()
                .map(w -> w.getBlockName())
                .distinct()
                .collect(Collectors.toList());

        for (String block : blocks) {
            try {
                Double totalOil = productionDataRepository.sumOilVolumeByDateAndBlock(date, block);
                Double totalWater = injectionDataRepository.sumWaterVolumeByDateAndBlock(date, block);
                Double avgWaterCut = productionDataRepository.avgWaterCutByDateAndBlock(date, block);

                if (totalOil == null) totalOil = 0.0;
                if (totalWater == null) totalWater = 0.0;
                if (avgWaterCut == null) avgWaterCut = 0.0;

                BlockDailySummary summary = new BlockDailySummary();
                summary.setBlockName(block);
                summary.setSummaryDate(date);
                summary.setTotalOilProduction(Math.round(totalOil * 100.0) / 100.0);
                summary.setTotalWaterInjection(Math.round(totalWater * 100.0) / 100.0);
                summary.setAverageWaterCut(Math.round(avgWaterCut * 100.0) / 100.0);

                summaryRepository.save(summary);
                log.info("Generated summary for block: {}, date: {}, oil: {} t", block, date, totalOil);

            } catch (Exception e) {
                log.error("Failed to generate summary for block: {}", block, e);
            }
        }
    }

    public BlockSummaryDTO getLatestSummary(String blockName) {
        if (blockName == null || "ALL".equalsIgnoreCase(blockName)) {
            LocalDate latestDate = LocalDate.now();
            for (int i = 0; i < 7; i++) {
                LocalDate checkDate = latestDate.minusDays(i);
                BlockSummaryDTO summary = summaryRepository.findAllBlocksSummaryByDate(checkDate);
                if (summary != null && summary.getTotalOilProduction() > 0) {
                    return summary;
                }
            }
            return new BlockSummaryDTO();
        }

        List<BlockDailySummary> summaries = summaryRepository
                .findByBlockNameOrderBySummaryDateDesc(blockName);
        if (summaries.isEmpty()) {
            return new BlockSummaryDTO();
        }
        return convertToDTO(summaries.get(0));
    }

    public List<BlockSummaryDTO> getSummaryHistory(String blockName, int days) {
        LocalDate endDate = LocalDate.now();
        LocalDate startDate = endDate.minusDays(days);

        List<BlockDailySummary> summaries;
        if (blockName == null || "ALL".equalsIgnoreCase(blockName)) {
            summaries = summaryRepository.findFromDate(startDate);
            return summaries.stream()
                    .collect(Collectors.groupingBy(BlockDailySummary::getSummaryDate))
                    .entrySet().stream()
                    .map(entry -> {
                        BlockSummaryDTO dto = new BlockSummaryDTO();
                        dto.setBlockName("ALL");
                        dto.setSummaryDate(entry.getKey());
                        dto.setTotalOilProduction(entry.getValue().stream()
                                .mapToDouble(BlockDailySummary::getTotalOilProduction).sum());
                        dto.setTotalWaterInjection(entry.getValue().stream()
                                .mapToDouble(BlockDailySummary::getTotalWaterInjection).sum());
                        dto.setAverageWaterCut(entry.getValue().stream()
                                .mapToDouble(BlockDailySummary::getAverageWaterCut).average().orElse(0.0));
                        return dto;
                    })
                    .sorted((a, b) -> b.getSummaryDate().compareTo(a.getSummaryDate()))
                    .collect(Collectors.toList());
        } else {
            summaries = summaryRepository.findByBlockNameOrderBySummaryDateDesc(blockName);
            return summaries.stream()
                    .filter(s -> !s.getSummaryDate().isBefore(startDate))
                    .map(this::convertToDTO)
                    .collect(Collectors.toList());
        }
    }

    public BlockSummaryDTO getSummaryByDate(String blockName, LocalDate date) {
        if (blockName == null || "ALL".equalsIgnoreCase(blockName)) {
            return summaryRepository.findAllBlocksSummaryByDate(date);
        }
        return summaryRepository.findByBlockNameAndSummaryDate(blockName, date)
                .map(this::convertToDTO)
                .orElse(null);
    }

    private BlockSummaryDTO convertToDTO(BlockDailySummary summary) {
        BlockSummaryDTO dto = new BlockSummaryDTO();
        dto.setBlockName(summary.getBlockName());
        dto.setSummaryDate(summary.getSummaryDate());
        dto.setTotalOilProduction(summary.getTotalOilProduction());
        dto.setTotalWaterInjection(summary.getTotalWaterInjection());
        dto.setAverageWaterCut(summary.getAverageWaterCut());
        return dto;
    }
}
