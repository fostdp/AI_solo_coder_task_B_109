package com.smart.oilfield.repository;

import com.smart.oilfield.entity.InjectionProductionRelation;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;
import org.springframework.stereotype.Repository;

import java.util.List;

@Repository
public interface InjectionProductionRelationRepository extends JpaRepository<InjectionProductionRelation, Long> {

    List<InjectionProductionRelation> findByInjectionWellId(String injectionWellId);

    List<InjectionProductionRelation> findByProductionWellId(String productionWellId);

    List<InjectionProductionRelation> findByEffectivenessType(String effectivenessType);

    @Query("SELECT r FROM InjectionProductionRelation r " +
           "WHERE r.injectionWellId = :injectionWellId " +
           "OR r.productionWellId = :productionWellId")
    List<InjectionProductionRelation> findByWellId(@Param("injectionWellId") String injectionWellId,
                                                    @Param("productionWellId") String productionWellId);

    @Query("SELECT r FROM InjectionProductionRelation r " +
           "JOIN Well i ON r.injectionWellId = i.wellId " +
           "JOIN Well p ON r.productionWellId = p.wellId " +
           "WHERE i.blockName = :blockName AND p.blockName = :blockName")
    List<InjectionProductionRelation> findByBlockName(@Param("blockName") String blockName);
}
