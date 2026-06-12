package com.smart.oilfield.dto;

import lombok.Data;
import java.time.LocalDate;

@Data
public class AllocationSuggestionDTO {
    private Long id;
    private String wellId;
    private String wellName;
    private LocalDate suggestionDate;
    private Double currentWaterVolume;
    private Double suggestedWaterVolume;
    private String adjustmentDirection;
    private Double adjustmentAmount;
    private String reason;
    private Double longitude;
    private Double latitude;
}
