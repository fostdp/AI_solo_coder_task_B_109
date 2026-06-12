package com.smart.oilfield.dto;

import lombok.Data;
import java.util.List;

@Data
public class WellTrendDTO {
    private String wellId;
    private String wellName;
    private String wellType;
    private List<String> dates;
    private List<Double> oilVolumes;
    private List<Double> waterVolumes;
    private List<Double> waterCuts;
    private List<Double> pressures;
}
