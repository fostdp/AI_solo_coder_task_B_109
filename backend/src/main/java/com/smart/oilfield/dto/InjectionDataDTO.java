package com.smart.oilfield.dto;

import lombok.Data;
import java.time.LocalDate;

@Data
public class InjectionDataDTO {
    private String wellId;
    private LocalDate reportDate;
    private Double waterVolume;
    private Double injectionPressure;
    private Double waterAbsorptionIndex;
}
