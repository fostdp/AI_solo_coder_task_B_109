package com.smart.oilfield.service;

import com.smart.oilfield.dto.AllocationSuggestionDTO;
import com.smart.oilfield.entity.*;
import com.smart.oilfield.repository.*;
import lombok.extern.slf4j.Slf4j;
import org.apache.commons.math3.optim.MaxIter;
import org.apache.commons.math3.optim.PointValuePair;
import org.apache.commons.math3.optim.linear.*;
import org.apache.commons.math3.optim.nonlinear.scalar.GoalType;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Service;

import java.time.LocalDate;
import java.util.*;
import java.util.stream.Collectors;

@Slf4j
@Service
public class AllocationOptimizationService {

    @Autowired
    private WellRepository wellRepository;

    @Autowired
    private WaterInjectionDataRepository injectionDataRepository;

    @Autowired
    private ProductionDataRepository productionDataRepository;

    @Autowired
    private InjectionProductionRelationRepository relationRepository;

    @Autowired
    private AllocationSuggestionRepository suggestionRepository;

    @Value("${allocation.days-interval:7}")
    private Integer daysInterval;

    @Value("${allocation.model-version:1.0.0}")
    private String modelVersion;

    private static final int HISTORY_DAYS = 30;
    private static final double MAX_WATER_INCREASE_RATE = 0.2;
    private static final double MAX_WATER_DECREASE_RATE = 0.3;

    @Scheduled(cron = "${allocation.schedule:0 0 2 * * ?}")
    public void scheduledAllocationOptimization() {
        LocalDate today = LocalDate.now();
        LocalDate lastRun = today.minusDays(daysInterval);
        
        boolean hasRecentRun = !suggestionRepository.findFromDate(lastRun).isEmpty();
        if (hasRecentRun) {
            log.info("Skipping allocation optimization - recent run exists");
            return;
        }

        log.info("Starting allocation optimization...");
        List<String> blocks = wellRepository.findAll().stream()
                .map(Well::getBlockName)
                .distinct()
                .collect(Collectors.toList());

        for (String block : blocks) {
            try {
                optimizeBlockAllocation(block, today);
            } catch (Exception e) {
                log.error("Failed to optimize allocation for block: {}", block, e);
            }
        }
        log.info("Allocation optimization completed");
    }

    public List<AllocationSuggestion> optimizeBlockAllocation(String blockName, LocalDate date) {
        log.info("Optimizing allocation for block: {}", blockName);

        List<Well> injectionWells = wellRepository
                .findByWellTypeAndBlockName("INJECTION", blockName)
                .stream()
                .filter(w -> "ACTIVE".equals(w.getStatus()))
                .collect(Collectors.toList());

        List<Well> productionWells = wellRepository
                .findByWellTypeAndBlockName("PRODUCTION", blockName)
                .stream()
                .filter(w -> "ACTIVE".equals(w.getStatus()))
                .collect(Collectors.toList());

        if (injectionWells.isEmpty() || productionWells.isEmpty()) {
            log.warn("No active wells in block: {}", blockName);
            return Collections.emptyList();
        }

        Map<String, Double> currentInjection = getCurrentInjectionVolumes(injectionWells);
        Map<String, double[]> waterFloodParams = calculateWaterFloodParameters(blockName);
        Map<String, List<InjectionProductionRelation>> relations = getInjectionRelations(injectionWells);

        double[] optimalVolumes = solveLinearProgram(
                injectionWells,
                productionWells,
                currentInjection,
                waterFloodParams,
                relations
        );

        List<AllocationSuggestion> suggestions = new ArrayList<>();
        for (int i = 0; i < injectionWells.size(); i++) {
            Well well = injectionWells.get(i);
            double current = currentInjection.getOrDefault(well.getWellId(), 0.0);
            double suggested = optimalVolumes[i];
            double adjustment = suggested - current;

            AllocationSuggestion suggestion = new AllocationSuggestion();
            suggestion.setWellId(well.getWellId());
            suggestion.setSuggestionDate(date);
            suggestion.setCurrentWaterVolume(current);
            suggestion.setSuggestedWaterVolume(Math.round(suggested * 100.0) / 100.0);
            suggestion.setAdjustmentAmount(Math.round(adjustment * 100.0) / 100.0);

            if (adjustment > 1.0) {
                suggestion.setAdjustmentDirection("INCREASE");
            } else if (adjustment < -1.0) {
                suggestion.setAdjustmentDirection("DECREASE");
            } else {
                suggestion.setAdjustmentDirection("KEEP");
            }

            suggestion.setReason(generateReason(well, current, suggested, relations.get(well.getWellId())));
            suggestion.setModelVersion(modelVersion);

            suggestions.add(suggestionRepository.save(suggestion));
        }

        log.info("Generated {} allocation suggestions for block: {}", suggestions.size(), blockName);
        return suggestions;
    }

    private Map<String, Double> getCurrentInjectionVolumes(List<Well> injectionWells) {
        Map<String, Double> volumes = new HashMap<>();
        for (Well well : injectionWells) {
            WaterInjectionData latest = injectionDataRepository.findLatestByWellId(well.getWellId());
            volumes.put(well.getWellId(), latest != null ? latest.getWaterVolume() : 100.0);
        }
        return volumes;
    }

    private Map<String, double[]> calculateWaterFloodParameters(String blockName) {
        Map<String, double[]> params = new HashMap<>();
        LocalDate endDate = LocalDate.now();
        LocalDate startDate = endDate.minusDays(HISTORY_DAYS);

        List<Well> productionWells = wellRepository.findByWellTypeAndBlockName("PRODUCTION", blockName);
        for (Well well : productionWells) {
            List<ProductionData> data = productionDataRepository
                    .findByWellIdAndReportDateBetweenOrderByReportDate(well.getWellId(), startDate, endDate);

            if (data.size() < 7) {
                params.put(well.getWellId(), new double[]{0.015, 0.0, 0.0});
                continue;
            }

            double[] regression = performWaterFloodRegression(data);
            params.put(well.getWellId(), regression);
        }
        return params;
    }

    private double[] performWaterFloodRegression(List<ProductionData> data) {
        int n = data.size();
        double[] x = new double[n];
        double[] y = new double[n];

        double cumulativeOil = 0;
        double cumulativeWater = 0;

        for (int i = 0; i < n; i++) {
            ProductionData d = data.get(i);
            cumulativeOil += d.getOilVolume();
            cumulativeWater += (d.getLiquidVolume() - d.getOilVolume());
            x[i] = cumulativeOil;
            y[i] = Math.log10(Math.max(cumulativeWater, 1.0));
        }

        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        for (int i = 0; i < n; i++) {
            sumX += x[i];
            sumY += y[i];
            sumXY += x[i] * y[i];
            sumX2 += x[i] * x[i];
        }

        double slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
        double intercept = (sumY - slope * sumX) / n;
        double waterCutRate = calculateWaterCutRiseRate(data);

        return new double[]{slope, intercept, waterCutRate};
    }

    private double calculateWaterCutRiseRate(List<ProductionData> data) {
        if (data.size() < 2) return 0.001;

        double firstWaterCut = data.get(0).getWaterCut();
        double lastWaterCut = data.get(data.size() - 1).getWaterCut();
        int days = data.size();

        return Math.max((lastWaterCut - firstWaterCut) / days / 100.0, 0.001);
    }

    private Map<String, List<InjectionProductionRelation>> getInjectionRelations(List<Well> injectionWells) {
        Map<String, List<InjectionProductionRelation>> relations = new HashMap<>();
        for (Well well : injectionWells) {
            relations.put(well.getWellId(),
                    relationRepository.findByInjectionWellId(well.getWellId()));
        }
        return relations;
    }

    private double[] solveLinearProgram(
            List<Well> injectionWells,
            List<Well> productionWells,
            Map<String, Double> currentInjection,
            Map<String, double[]> waterFloodParams,
            Map<String, List<InjectionProductionRelation>> relations) {

        int n = injectionWells.size();
        double[] coefficients = new double[n];

        for (int i = 0; i < n; i++) {
            Well injWell = injectionWells.get(i);
            List<InjectionProductionRelation> wellRelations = relations.get(injWell.getWellId());

            double oilGainCoefficient = 0;
            double waterCutPenalty = 0;

            for (InjectionProductionRelation rel : wellRelations) {
                double[] params = waterFloodParams.get(rel.getProductionWellId());
                if (params != null) {
                    double effectiveness = rel.getEffectivenessDegree() != null ?
                            rel.getEffectivenessDegree() / 100.0 : 0.5;
                    oilGainCoefficient += params[0] * effectiveness * 1000;
                    waterCutPenalty += params[2] * effectiveness * 500;
                }
            }

            coefficients[i] = oilGainCoefficient - waterCutPenalty;
        }

        LinearObjectiveFunction objective = new LinearObjectiveFunction(coefficients, 0);

        List<LinearConstraint> constraints = new ArrayList<>();

        double[] equalityCoeff = new double[n];
        double totalCurrentInjection = 0;
        for (int i = 0; i < n; i++) {
            equalityCoeff[i] = 1.0;
            totalCurrentInjection += currentInjection.getOrDefault(injectionWells.get(i).getWellId(), 0.0);
        }
        constraints.add(new LinearConstraint(equalityCoeff, Relationship.EQ, totalCurrentInjection));

        for (int i = 0; i < n; i++) {
            double current = currentInjection.getOrDefault(injectionWells.get(i).getWellId(), 0.0);
            double maxIncrease = current * (1 + MAX_WATER_INCREASE_RATE);
            double maxDecrease = current * (1 - MAX_WATER_DECREASE_RATE);

            double[] upperBound = new double[n];
            upperBound[i] = 1.0;
            constraints.add(new LinearConstraint(upperBound, Relationship.LEQ, maxIncrease));

            double[] lowerBound = new double[n];
            lowerBound[i] = 1.0;
            constraints.add(new LinearConstraint(lowerBound, Relationship.GEQ, Math.max(maxDecrease, 10.0)));
        }

        try {
            SimplexSolver solver = new SimplexSolver();
            PointValuePair solution = solver.optimize(
                    new MaxIter(1000),
                    objective,
                    new LinearConstraintSet(constraints),
                    GoalType.MAXIMIZE,
                    new NonNegativeConstraint(true)
            );

            log.info("Optimization successful, objective value: {}", solution.getValue());
            return solution.getPoint();

        } catch (Exception e) {
            log.error("Linear programming failed, using current values", e);
            double[] result = new double[n];
            for (int i = 0; i < n; i++) {
                result[i] = currentInjection.getOrDefault(injectionWells.get(i).getWellId(), 0.0);
            }
            return result;
        }
    }

    private String generateReason(Well well, double current, double suggested,
                                   List<InjectionProductionRelation> relations) {
        double percentChange = ((suggested - current) / current) * 100;
        StringBuilder reason = new StringBuilder();

        if (Math.abs(percentChange) < 1) {
            reason.append("当前注水量合理，建议保持");
        } else if (percentChange > 0) {
            reason.append(String.format("建议增加注水量 %.1f%%", percentChange));
            if (relations != null && !relations.isEmpty()) {
                long highEffCount = relations.stream()
                        .filter(r -> "HIGH".equals(r.getEffectivenessType())).count();
                if (highEffCount > 0) {
                    reason.append(String.format("，该井连通 %d 口高效受效采油井", highEffCount));
                }
            }
        } else {
            reason.append(String.format("建议减少注水量 %.1f%%", -percentChange));
            if (relations != null && !relations.isEmpty()) {
                long lowEffCount = relations.stream()
                        .filter(r -> "LOW".equals(r.getEffectivenessType())).count();
                if (lowEffCount > 0) {
                    reason.append(String.format("，该井有 %d 口低效受效井，需控制注水", lowEffCount));
                }
            }
        }

        return reason.toString();
    }

    public List<AllocationSuggestionDTO> getLatestSuggestions() {
        List<AllocationSuggestion> suggestions = suggestionRepository.findLatestSuggestions();
        return convertToDTO(suggestions);
    }

    public List<AllocationSuggestionDTO> getSuggestionsByDate(LocalDate date) {
        List<AllocationSuggestion> suggestions = suggestionRepository.findBySuggestionDate(date);
        return convertToDTO(suggestions);
    }

    public List<AllocationSuggestionDTO> getSuggestionsByWell(String wellId) {
        List<AllocationSuggestion> suggestions = suggestionRepository
                .findByWellIdOrderBySuggestionDateDesc(wellId);
        return convertToDTO(suggestions);
    }

    private List<AllocationSuggestionDTO> convertToDTO(List<AllocationSuggestion> suggestions) {
        return suggestions.stream().map(s -> {
            AllocationSuggestionDTO dto = new AllocationSuggestionDTO();
            dto.setId(s.getId());
            dto.setWellId(s.getWellId());
            dto.setSuggestionDate(s.getSuggestionDate());
            dto.setCurrentWaterVolume(s.getCurrentWaterVolume());
            dto.setSuggestedWaterVolume(s.getSuggestedWaterVolume());
            dto.setAdjustmentDirection(s.getAdjustmentDirection());
            dto.setAdjustmentAmount(s.getAdjustmentAmount());
            dto.setReason(s.getReason());

            wellRepository.findById(s.getWellId()).ifPresent(well -> {
                dto.setWellName(well.getWellName());
                dto.setLongitude(well.getLongitude());
                dto.setLatitude(well.getLatitude());
            });

            return dto;
        }).collect(Collectors.toList());
    }

    public void runOptimizationNow() {
        log.info("Manual optimization triggered");
        List<String> blocks = wellRepository.findAll().stream()
                .map(Well::getBlockName)
                .distinct()
                .collect(Collectors.toList());

        LocalDate today = LocalDate.now();
        for (String block : blocks) {
            try {
                optimizeBlockAllocation(block, today);
            } catch (Exception e) {
                log.error("Failed to optimize allocation for block: {}", block, e);
            }
        }
    }
}
