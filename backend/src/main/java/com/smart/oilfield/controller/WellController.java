package com.smart.oilfield.controller;

import com.smart.oilfield.dto.WellDTO;
import com.smart.oilfield.dto.WellTrendDTO;
import com.smart.oilfield.entity.Well;
import com.smart.oilfield.service.WellService;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

import java.util.HashMap;
import java.util.List;
import java.util.Map;

@RestController
@RequestMapping("/wells")
public class WellController {

    @Autowired
    private WellService wellService;

    @GetMapping
    public ResponseEntity<List<WellDTO>> getAllWells(
            @RequestParam(required = false) String wellType,
            @RequestParam(required = false) String blockName) {

        List<WellDTO> wells;
        if (wellType != null && blockName != null) {
            wells = wellService.getWellsByBlock(blockName).stream()
                    .filter(w -> wellType.equals(w.getWellType()))
                    .toList();
        } else if (wellType != null) {
            wells = wellService.getWellsByType(wellType);
        } else if (blockName != null) {
            wells = wellService.getWellsByBlock(blockName);
        } else {
            wells = wellService.getAllWells();
        }
        return ResponseEntity.ok(wells);
    }

    @GetMapping("/{wellId}")
    public ResponseEntity<WellDTO> getWellById(@PathVariable String wellId) {
        WellDTO well = wellService.getWellById(wellId);
        if (well == null) {
            return ResponseEntity.notFound().build();
        }
        return ResponseEntity.ok(well);
    }

    @GetMapping("/{wellId}/trend")
    public ResponseEntity<WellTrendDTO> getWellTrend(
            @PathVariable String wellId,
            @RequestParam(defaultValue = "90") int days) {
        WellTrendDTO trend = wellService.getWellTrend(wellId, days);
        if (trend == null) {
            return ResponseEntity.notFound().build();
        }
        return ResponseEntity.ok(trend);
    }

    @GetMapping("/blocks")
    public ResponseEntity<Map<String, Object>> getBlocks() {
        Map<String, Object> result = new HashMap<>();
        result.put("blocks", wellService.getAllBlockNames());
        return ResponseEntity.ok(result);
    }

    @PostMapping
    public ResponseEntity<Well> createWell(@RequestBody Well well) {
        Well created = wellService.saveWell(well);
        return ResponseEntity.ok(created);
    }

    @PutMapping("/{wellId}")
    public ResponseEntity<Well> updateWell(@PathVariable String wellId, @RequestBody Well well) {
        if (!wellService.getWellById(wellId).getWellId().equals(wellId)) {
            return ResponseEntity.badRequest().build();
        }
        well.setWellId(wellId);
        Well updated = wellService.saveWell(well);
        return ResponseEntity.ok(updated);
    }

    @DeleteMapping("/{wellId}")
    public ResponseEntity<Void> deleteWell(@PathVariable String wellId) {
        wellService.deleteWell(wellId);
        return ResponseEntity.ok().build();
    }
}
