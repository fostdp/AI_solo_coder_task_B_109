package com.smart.oilfield.dto;

import lombok.Data;
import java.time.LocalDate;

@Data
public class ProductionDataDTO {
    private String wellId;
    private LocalDate reportDate;
    private Double liquidVolume;
    private Double oilVolume;
    private Double waterCut;
    private Double dynamicFluidLevel;
}
