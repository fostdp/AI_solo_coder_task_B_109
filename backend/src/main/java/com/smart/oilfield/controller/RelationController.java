package com.smart.oilfield.controller;

import com.smart.oilfield.entity.InjectionProductionRelation;
import com.smart.oilfield.entity.Well;
import com.smart.oilfield.repository.InjectionProductionRelationRepository;
import com.smart.oilfield.repository.WellRepository;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

@RestController
@RequestMapping("/relations")
public class RelationController {

    @Autowired
    private InjectionProductionRelationRepository relationRepository;

    @Autowired
    private WellRepository wellRepository;

    @GetMapping
    public ResponseEntity<Map<String, Object>> getAllRelations(
            @RequestParam(required = false) String blockName,
            @RequestParam(required = false) String effectivenessType) {

        List<InjectionProductionRelation> relations;
        if (blockName != null) {
            relations = relationRepository.findByBlockName(blockName);
        } else {
            relations = relationRepository.findAll();
        }

        if (effectivenessType != null) {
            relations = relations.stream()
                    .filter(r -> effectivenessType.equals(r.getEffectivenessType()))
                    .toList();
        }

        List<Map<String, Object>> result = new ArrayList<>();
        for (InjectionProductionRelation rel : relations) {
            Map<String, Object> relMap = new HashMap<>();
            relMap.put("id", rel.getId());
            relMap.put("injectionWellId", rel.getInjectionWellId());
            relMap.put("productionWellId", rel.getProductionWellId());
            relMap.put("effectivenessType", rel.getEffectivenessType());
            relMap.put("effectivenessDegree", rel.getEffectivenessDegree());
            relMap.put("distance", rel.getDistance());

            wellRepository.findById(rel.getInjectionWellId()).ifPresent(w -> {
                relMap.put("injectionLongitude", w.getLongitude());
                relMap.put("injectionLatitude", w.getLatitude());
            });

            wellRepository.findById(rel.getProductionWellId()).ifPresent(w -> {
                relMap.put("productionLongitude", w.getLongitude());
                relMap.put("productionLatitude", w.getLatitude());
            });

            result.add(relMap);
        }

        Map<String, Object> response = new HashMap<>();
        response.put("relations", result);
        response.put("totalCount", result.size());
        response.put("highCount", (int) result.stream()
                .filter(r -> "HIGH".equals(r.get("effectivenessType"))).count());
        response.put("mediumCount", (int) result.stream()
                .filter(r -> "MEDIUM".equals(r.get("effectivenessType"))).count());
        response.put("lowCount", (int) result.stream()
                .filter(r -> "LOW".equals(r.get("effectivenessType"))).count());

        return ResponseEntity.ok(response);
    }

    @GetMapping("/injection/{wellId}")
    public ResponseEntity<List<InjectionProductionRelation>> getRelationsByInjectionWell(
            @PathVariable String wellId) {
        List<InjectionProductionRelation> relations = relationRepository.findByInjectionWellId(wellId);
        return ResponseEntity.ok(relations);
    }

    @GetMapping("/production/{wellId}")
    public ResponseEntity<List<InjectionProductionRelation>> getRelationsByProductionWell(
            @PathVariable String wellId) {
        List<InjectionProductionRelation> relations = relationRepository.findByProductionWellId(wellId);
        return ResponseEntity.ok(relations);
    }

    @GetMapping("/map-data")
    public ResponseEntity<Map<String, Object>> getMapRelationData(
            @RequestParam(required = false) String blockName) {

        List<InjectionProductionRelation> relations;
        if (blockName != null) {
            relations = relationRepository.findByBlockName(blockName);
        } else {
            relations = relationRepository.findAll();
        }

        List<Map<String, Object>> lines = new ArrayList<>();
        for (InjectionProductionRelation rel : relations) {
            Map<String, Object> line = new HashMap<>();

            Well injWell = wellRepository.findById(rel.getInjectionWellId()).orElse(null);
            Well prodWell = wellRepository.findById(rel.getProductionWellId()).orElse(null);

            if (injWell != null && prodWell != null) {
                line.put("type", "LineString");
                line.put("injectionWellId", rel.getInjectionWellId());
                line.put("productionWellId", rel.getProductionWellId());
                line.put("effectivenessType", rel.getEffectivenessType());
                line.put("effectivenessDegree", rel.getEffectivenessDegree());

                double[][] coordinates = {
                    {injWell.getLongitude(), injWell.getLatitude()},
                    {prodWell.getLongitude(), prodWell.getLatitude()}
                };
                line.put("coordinates", coordinates);

                String lineColor;
                switch (rel.getEffectivenessType()) {
                    case "HIGH" -> lineColor = "#00FF00";
                    case "MEDIUM" -> lineColor = "#FFFF00";
                    case "LOW" -> lineColor = "#FF0000";
                    default -> lineColor = "#888888";
                }
                line.put("color", lineColor);
                line.put("distance", rel.getDistance());

                lines.add(line);
            }
        }

        Map<String, Object> result = new HashMap<>();
        result.put("lines", lines);
        result.put("totalCount", lines.size());

        return ResponseEntity.ok(result);
    }

    @PostMapping
    public ResponseEntity<InjectionProductionRelation> createRelation(
            @RequestBody InjectionProductionRelation relation) {
        InjectionProductionRelation created = relationRepository.save(relation);
        return ResponseEntity.ok(created);
    }

    @PutMapping("/{id}")
    public ResponseEntity<InjectionProductionRelation> updateRelation(
            @PathVariable Long id,
            @RequestBody InjectionProductionRelation relation) {
        return relationRepository.findById(id).map(existing -> {
            relation.setId(id);
            InjectionProductionRelation updated = relationRepository.save(relation);
            return ResponseEntity.ok(updated);
        }).orElse(ResponseEntity.notFound().build());
    }

    @DeleteMapping("/{id}")
    public ResponseEntity<Void> deleteRelation(@PathVariable Long id) {
        if (!relationRepository.existsById(id)) {
            return ResponseEntity.notFound().build();
        }
        relationRepository.deleteById(id);
        return ResponseEntity.ok().build();
    }
}
