package com.smart.oilfield.dto;

import lombok.Data;

@Data
public class WellDTO {
    private String wellId;
    private String wellName;
    private String wellType;
    private String blockName;
    private Double designPressure;
    private Double longitude;
    private Double latitude;
    private String status;
    private Double latestWaterVolume;
    private Double latestInjectionPressure;
    private Double latestOilVolume;
    private Double latestWaterCut;
}
