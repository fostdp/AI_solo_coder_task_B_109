package com.smart.oilfield;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.scheduling.annotation.EnableScheduling;

@SpringBootApplication
@EnableScheduling
public class SmartOilfieldApplication {
    public static void main(String[] args) {
        SpringApplication.run(SmartOilfieldApplication.class, args);
    }
}
