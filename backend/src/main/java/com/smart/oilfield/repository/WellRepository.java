package com.smart.oilfield.repository;

import com.smart.oilfield.entity.Well;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;
import org.springframework.stereotype.Repository;

import java.util.List;

@Repository
public interface WellRepository extends JpaRepository<Well, String> {

    List<Well> findByWellType(String wellType);

    List<Well> findByBlockName(String blockName);

    List<Well> findByWellTypeAndBlockName(String wellType, String blockName);

    @Query("SELECT w FROM Well w WHERE w.status = 'ACTIVE'")
    List<Well> findAllActiveWells();

    @Query("SELECT w FROM Well w WHERE w.wellType = :wellType AND w.status = 'ACTIVE'")
    List<Well> findActiveWellsByType(@Param("wellType") String wellType);
}
