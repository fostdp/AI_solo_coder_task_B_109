package com.smart.oilfield.service;

import com.smart.oilfield.entity.Alarm;
import com.smart.oilfield.entity.ProductionData;
import com.smart.oilfield.entity.WaterInjectionData;
import com.smart.oilfield.entity.Well;
import com.smart.oilfield.repository.AlarmRepository;
import com.smart.oilfield.repository.ProductionDataRepository;
import com.smart.oilfield.repository.WaterInjectionDataRepository;
import com.smart.oilfield.repository.WellRepository;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Service;

import java.time.LocalDate;
import java.time.LocalDateTime;
import java.util.List;
import java.util.UUID;

@Slf4j
@Service
public class AlarmService {

    @Autowired
    private AlarmRepository alarmRepository;

    @Autowired
    private WellRepository wellRepository;

    @Autowired
    private ProductionDataRepository productionDataRepository;

    @Autowired
    private WaterInjectionDataRepository injectionDataRepository;

    @Autowired
    private MqttMessageService mqttMessageService;

    @Value("${alarm.water-cut-rise-threshold:5.0}")
    private Double waterCutRiseThreshold;

    @Value("${alarm.pressure-threshold-ratio:0.8}")
    private Double pressureThresholdRatio;

    @Scheduled(cron = "${alarm.check-schedule:0 0 1 * * ?}")
    public void scheduledAlarmCheck() {
        log.info("Starting scheduled alarm check...");
        checkWaterCutAlarms();
        checkPressureAlarms();
        pushUnsentAlarms();
        log.info("Scheduled alarm check completed");
    }

    public void checkWaterCutAlarms() {
        log.info("Checking water cut alarms for all production wells...");
        List<Well> productionWells = wellRepository.findActiveWellsByType("PRODUCTION");
        LocalDate today = LocalDate.now();
        LocalDate oneMonthAgo = today.minusMonths(1);

        for (Well well : productionWells) {
            try {
                ProductionData latestData = productionDataRepository.findLatestByWellId(well.getWellId());
                if (latestData == null) continue;

                List<ProductionData> monthAgoData = productionDataRepository
                        .findByWellIdAndReportDateBetweenOrderByReportDate(
                                well.getWellId(), oneMonthAgo, oneMonthAgo.plusDays(3));

                if (monthAgoData.isEmpty()) continue;

                ProductionData oldData = monthAgoData.get(0);
                double waterCutRise = latestData.getWaterCut() - oldData.getWaterCut();

                if (waterCutRise > waterCutRiseThreshold) {
                    Alarm alarm = createWaterCutAlarm(well, latestData.getWaterCut(), oldData.getWaterCut());
                    alarm = alarmRepository.save(alarm);
                    log.warn("Water cut alarm created for well: {}, rise: {}%", well.getWellId(), waterCutRise);
                }
            } catch (Exception e) {
                log.error("Error checking water cut alarm for well: {}", well.getWellId(), e);
            }
        }
    }

    public void checkPressureAlarms() {
        log.info("Checking pressure alarms for all injection wells...");
        List<Well> injectionWells = wellRepository.findActiveWellsByType("INJECTION");

        for (Well well : injectionWells) {
            try {
                if (well.getDesignPressure() == null) continue;

                WaterInjectionData latestData = injectionDataRepository.findLatestByWellId(well.getWellId());
                if (latestData == null) continue;

                double threshold = well.getDesignPressure() * pressureThresholdRatio;

                if (latestData.getInjectionPressure() > threshold) {
                    Alarm alarm = createPressureAlarm(well, latestData.getInjectionPressure(), threshold);
                    alarm = alarmRepository.save(alarm);
                    log.warn("Pressure alarm created for well: {}, pressure: {} MPa", well.getWellId(), latestData.getInjectionPressure());
                }
            } catch (Exception e) {
                log.error("Error checking pressure alarm for well: {}", well.getWellId(), e);
            }
        }
    }

    private Alarm createWaterCutAlarm(Well well, double currentWaterCut, double previousWaterCut) {
        Alarm alarm = new Alarm();
        alarm.setAlarmId(UUID.randomUUID().toString());
        alarm.setWellId(well.getWellId());
        alarm.setAlarmLevel("LEVEL_1");
        alarm.setAlarmType("WATER_CUT_RISE");
        alarm.setAlarmMessage(String.format(
                "%s 含水率月上升超过阈值，当前: %.2f%%, 上月同期: %.2f%%, 上升: %.2f%%",
                well.getWellName(), currentWaterCut, previousWaterCut, currentWaterCut - previousWaterCut));
        alarm.setAlarmValue(currentWaterCut - previousWaterCut);
        alarm.setThresholdValue(waterCutRiseThreshold);
        alarm.setAlarmTime(LocalDateTime.now());
        return alarm;
    }

    private Alarm createPressureAlarm(Well well, double currentPressure, double threshold) {
        Alarm alarm = new Alarm();
        alarm.setAlarmId(UUID.randomUUID().toString());
        alarm.setWellId(well.getWellId());
        alarm.setAlarmLevel("LEVEL_2");
        alarm.setAlarmType("PRESSURE_ANOMALY");
        alarm.setAlarmMessage(String.format(
                "%s 注水压力异常升高，当前: %.2f MPa, 阈值: %.2f MPa (设计压力的 %d%%)",
                well.getWellName(), currentPressure, threshold, (int)(pressureThresholdRatio * 100)));
        alarm.setAlarmValue(currentPressure);
        alarm.setThresholdValue(threshold);
        alarm.setAlarmTime(LocalDateTime.now());
        return alarm;
    }

    public void pushUnsentAlarms() {
        List<Alarm> unsentAlarms = alarmRepository.findByIsPushedFalse();
        log.info("Pushing {} unsent alarms via MQTT...", unsentAlarms.size());

        for (Alarm alarm : unsentAlarms) {
            try {
                mqttMessageService.pushAlarm(alarm);
                alarm.setIsPushed(true);
                alarmRepository.save(alarm);
            } catch (Exception e) {
                log.error("Failed to push alarm: {}", alarm.getAlarmId(), e);
            }
        }
    }

    public List<Alarm> getAllAlarms() {
        return alarmRepository.findAll();
    }

    public List<Alarm> getUnacknowledgedAlarms() {
        return alarmRepository.findByIsAcknowledgedFalseOrderByAlarmTimeDesc();
    }

    public List<Alarm> getAlarmsByWell(String wellId) {
        return alarmRepository.findByWellIdOrderByAlarmTimeDesc(wellId);
    }

    public List<Alarm> getAlarmsByLevel(String level) {
        return alarmRepository.findByAlarmLevelOrderByAlarmTimeDesc(level);
    }

    public Alarm acknowledgeAlarm(Long id) {
        return alarmRepository.findById(id).map(alarm -> {
            alarm.setIsAcknowledged(true);
            alarm.setAcknowledgeTime(LocalDateTime.now());
            return alarmRepository.save(alarm);
        }).orElse(null);
    }

    public Long getUnacknowledgedCount() {
        return alarmRepository.countUnacknowledgedAlarms();
    }
}
