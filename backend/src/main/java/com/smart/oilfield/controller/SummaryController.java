package com.smart.oilfield.controller;

import com.smart.oilfield.dto.BlockSummaryDTO;
import com.smart.oilfield.service.BlockSummaryService;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.format.annotation.DateTimeFormat;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

import java.time.LocalDate;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

@RestController
@RequestMapping("/summary")
public class SummaryController {

    @Autowired
    private BlockSummaryService summaryService;

    @GetMapping("/latest")
    public ResponseEntity<Map<String, Object>> getLatestSummary(
            @RequestParam(required = false, defaultValue = "ALL") String blockName) {
        BlockSummaryDTO summary = summaryService.getLatestSummary(blockName);

        Map<String, Object> result = new HashMap<>();
        result.put("totalOilProduction", summary.getTotalOilProduction());
        result.put("totalWaterInjection", summary.getTotalWaterInjection());
        result.put("averageWaterCut", summary.getAverageWaterCut());
        result.put("summaryDate", summary.getSummaryDate());
        result.put("blockName", summary.getBlockName());

        return ResponseEntity.ok(result);
    }

    @GetMapping("/history")
    public ResponseEntity<List<BlockSummaryDTO>> getSummaryHistory(
            @RequestParam(required = false, defaultValue = "ALL") String blockName,
            @RequestParam(defaultValue = "30") int days) {
        List<BlockSummaryDTO> history = summaryService.getSummaryHistory(blockName, days);
        return ResponseEntity.ok(history);
    }

    @GetMapping("/date")
    public ResponseEntity<BlockSummaryDTO> getSummaryByDate(
            @RequestParam(required = false, defaultValue = "ALL") String blockName,
            @RequestParam @DateTimeFormat(iso = DateTimeFormat.ISO.DATE) LocalDate date) {
        BlockSummaryDTO summary = summaryService.getSummaryByDate(blockName, date);
        if (summary == null) {
            return ResponseEntity.notFound().build();
        }
        return ResponseEntity.ok(summary);
    }

    @GetMapping("/core-indicators")
    public ResponseEntity<Map<String, Object>> getCoreIndicators(
            @RequestParam(required = false, defaultValue = "ALL") String blockName) {
        BlockSummaryDTO latest = summaryService.getLatestSummary(blockName);
        List<BlockSummaryDTO> history = summaryService.getSummaryHistory(blockName, 7);

        double prevOil = 0;
        double prevWater = 0;
        double prevWaterCut = 0;

        if (history.size() >= 2) {
            BlockSummaryDTO yesterday = history.get(1);
            prevOil = yesterday.getTotalOilProduction();
            prevWater = yesterday.getTotalWaterInjection();
            prevWaterCut = yesterday.getAverageWaterCut();
        }

        Map<String, Object> result = new HashMap<>();
        result.put("dailyOilProduction", latest.getTotalOilProduction());
        result.put("dailyWaterInjection", latest.getTotalWaterInjection());
        result.put("comprehensiveWaterCut", latest.getAverageWaterCut());
        result.put("summaryDate", latest.getSummaryDate());

        Map<String, Object> changes = new HashMap<>();
        changes.put("oilChange", prevOil > 0 ?
                ((latest.getTotalOilProduction() - prevOil) / prevOil * 100) : 0);
        changes.put("waterChange", prevWater > 0 ?
                ((latest.getTotalWaterInjection() - prevWater) / prevWater * 100) : 0);
        changes.put("waterCutChange", latest.getAverageWaterCut() - prevWaterCut);
        result.put("dayOverDayChanges", changes);

        Map<String, Object> injectionProductionRatio = new HashMap<>();
        double ratio = latest.getTotalWaterInjection() > 0 ?
                latest.getTotalOilProduction() / latest.getTotalWaterInjection() * 100 : 0;
        injectionProductionRatio.put("ratio", ratio);
        injectionProductionRatio.put("status", ratio > 10 ? "GOOD" : ratio > 5 ? "NORMAL" : "LOW");
        result.put("injectionProductionRatio", injectionProductionRatio);

        return ResponseEntity.ok(result);
    }
}
