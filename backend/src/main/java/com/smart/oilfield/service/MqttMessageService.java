package com.smart.oilfield.service;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.smart.oilfield.entity.Alarm;
import lombok.extern.slf4j.Slf4j;
import org.eclipse.paho.client.mqttv3.MqttClient;
import org.eclipse.paho.client.mqttv3.MqttMessage;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.integration.mqtt.core.MqttPahoClientFactory;
import org.springframework.stereotype.Service;

import javax.annotation.PostConstruct;
import javax.annotation.PreDestroy;

@Slf4j
@Service
public class MqttMessageService {

    @Autowired
    private MqttPahoClientFactory mqttClientFactory;

    @Autowired
    private ObjectMapper objectMapper;

    @Value("${mqtt.alarm-topic}")
    private String alarmTopic;

    @Value("${mqtt.client-id}")
    private String clientId;

    private MqttClient mqttClient;

    @PostConstruct
    public void init() {
        try {
            mqttClient = mqttClientFactory.getClientInstance(null, clientId + "-publisher");
            mqttClient.connect();
            log.info("MQTT publisher connected successfully");
        } catch (Exception e) {
            log.error("Failed to connect MQTT publisher", e);
        }
    }

    @PreDestroy
    public void destroy() {
        try {
            if (mqttClient != null && mqttClient.isConnected()) {
                mqttClient.disconnect();
                mqttClient.close();
            }
        } catch (Exception e) {
            log.error("Failed to disconnect MQTT publisher", e);
        }
    }

    public void pushAlarm(Alarm alarm) {
        try {
            if (mqttClient == null || !mqttClient.isConnected()) {
                log.warn("MQTT client not connected, attempting reconnect...");
                mqttClient = mqttClientFactory.getClientInstance(null, clientId + "-publisher");
                mqttClient.connect();
            }

            String payload = objectMapper.writeValueAsString(alarm);
            MqttMessage message = new MqttMessage(payload.getBytes());
            message.setQos(1);
            message.setRetained(false);

            mqttClient.publish(alarmTopic, message);
            log.info("Alarm pushed via MQTT: {} - {}", alarm.getAlarmId(), alarm.getAlarmMessage());

        } catch (Exception e) {
            log.error("Failed to push alarm via MQTT: {}", alarm.getAlarmId(), e);
        }
    }
}
