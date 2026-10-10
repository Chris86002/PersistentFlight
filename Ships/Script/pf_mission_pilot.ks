// Cooperative idle-CPU pilot. GPL-3.0. Run only after other guidance releases controls.
@LAZYGLOBAL OFF.
PARAMETER engineTag IS "".
LOCAL FUNCTION mission {
LOCAL command IS 0.
LOCAL FUNCTION stopBurn {
    SET ADDONS:PFR:WARPREADY TO FALSE.
    SET command TO 0. LOCK THROTTLE TO 0. WAIT 0.1.
    UNLOCK THROTTLE. SET SHIP:CONTROL:MAINTHROTTLE TO 0.
    UNLOCK STEERING. RCS OFF.
    IF engineTag <> "" {
        LOCAL engines IS LIST(). LIST ENGINES IN engines.
        FOR e IN engines { IF e:TAG = engineTag { e:SHUTDOWN. } }
    }
}
PRINT ADDONS:PFR:AUTOENABLE.
SET ADDONS:PFR:PILOTENGINETAG TO engineTag.
SET ADDONS:PFR:MONITORED TO TRUE.
SET ADDONS:PFR:WARPREADY TO FALSE.
PRINT "Mission pilot ready. Queue a node in Mission control.".
UNTIL FALSE {
    SET ADDONS:PFR:PILOTREADY TO TRUE.
    SET ADDONS:PFR:PHASE TO "Mission pilot ready".
    UNTIL ADDONS:PFR:NODEQUEUED {
        SET ADDONS:PFR:PILOTREADY TO TRUE.
        IF NOT ADDONS:PFR:MISSIONHOLD { ADDONS:PFR:RUNPILOTACTION(). }
        SET ADDONS:PFR:WARPUNTIL TO ADDONS:PFR:COASTUNTIL.
        SET ADDONS:PFR:WARPREADY TO NOT ADDONS:PFR:MISSIONHOLD AND ADDONS:PFR:COASTUNTIL > TIME:SECONDS+5.
        WAIT 0.5.
    }
    IF ADDONS:PFR:MISSIONHOLD { stopBurn(). WAIT 0.5. CONTINUE. }
    SET ADDONS:PFR:WARPREADY TO FALSE.
    IF engineTag <> "" {
        LOCAL engines IS LIST(). LIST ENGINES IN engines.
        FOR e IN engines { IF e:TAG = engineTag { e:ACTIVATE. } }
    }
    WAIT 0.1.
    LOCAL burnVector IS ADDONS:PFR:NODEVECTOR.
    IF NOT ADDONS:PFR:NODEQUEUED { CONTINUE. }
    IF SHIP:AVAILABLETHRUST < 0.001 {
        PRINT "Maneuver stopped: activate a suitable engine.".
        PRINT ADDONS:PFR:FINISHNODE(FALSE). stopBurn(). CONTINUE.
    }
    LOCAL acceleration IS SHIP:AVAILABLETHRUST/SHIP:MASS.
    LOCAL initialDV IS burnVector:MAG.
    LOCAL massFlow IS 0.
    LOCAL engines IS LIST(). LIST ENGINES IN engines.
    FOR e IN engines { IF e:AVAILABLETHRUST > 0 AND e:ISP > 0 {
        SET massFlow TO massFlow+e:AVAILABLETHRUST/(e:ISP*CONSTANT:G0).
    } }
    LOCAL duration IS initialDV/acceleration.
    LOCAL halfDuration IS duration/2.
    IF massFlow > 0 {
        LOCAL exhaust IS SHIP:AVAILABLETHRUST/massFlow.
        SET duration TO SHIP:MASS/massFlow*(1-CONSTANT:E^(-initialDV/exhaust)).
        SET halfDuration TO SHIP:MASS/massFlow*(1-CONSTANT:E^(-initialDV/(2*exhaust))).
    }
    IF TIME:SECONDS > ADDONS:PFR:NODEUT-halfDuration+5 {
        PRINT "Maneuver stopped: insufficient time to align before burn.".
        PRINT ADDONS:PFR:FINISHNODE(FALSE). stopBurn(). CONTINUE.
    }
    SAS OFF. RCS ON. LOCK THROTTLE TO command. LOCK STEERING TO burnVector.
    SET ADDONS:PFR:PHASE TO "Turning for maneuver".
    SET ADDONS:PFR:NODESTATE TO "Aligning".
    LOCAL aligned IS FALSE.
    LOCAL alignDeadline IS MIN(TIME:SECONDS+60,ADDONS:PFR:NODEUT-halfDuration+5).
    UNTIL aligned OR ADDONS:PFR:MISSIONHOLD OR NOT ADDONS:PFR:NODEQUEUED OR TIME:SECONDS > alignDeadline {
        SET ADDONS:PFR:PILOTREADY TO TRUE.
        LOCAL updatedVector IS ADDONS:PFR:NODEVECTOR.
        IF updatedVector:MAG > 0.001 { SET burnVector TO updatedVector. }
        SET aligned TO VANG(SHIP:FACING:VECTOR,burnVector) < 3.
        WAIT 0.1.
    }
    IF ADDONS:PFR:MISSIONHOLD { stopBurn(). CONTINUE. }
    IF NOT aligned { PRINT "Maneuver alignment stopped.". PRINT ADDONS:PFR:FINISHNODE(FALSE). stopBurn(). CONTINUE. }
    SET ADDONS:PFR:PHASE TO "Waiting for maneuver".
    SET ADDONS:PFR:NODESTATE TO "Waiting for burn".
    SET ADDONS:PFR:WARPUNTIL TO ADDONS:PFR:NODEUT-halfDuration-15.
    SET ADDONS:PFR:WARPREADY TO TRUE.
    UNTIL TIME:SECONDS >= ADDONS:PFR:NODEUT-halfDuration OR ADDONS:PFR:MISSIONHOLD OR NOT ADDONS:PFR:NODEQUEUED {
        SET ADDONS:PFR:PILOTREADY TO TRUE. WAIT 0.1.
    }
    SET ADDONS:PFR:WARPREADY TO FALSE.
    IF ADDONS:PFR:MISSIONHOLD { stopBurn(). CONTINUE. }
    LOCAL burnDeadline IS TIME:SECONDS+MAX(30,duration*2+30).
    LOCAL remainingDV IS initialDV.
    LOCAL lowestDV IS initialDV.
    SET ADDONS:PFR:PHASE TO "Maneuver burn".
    SET ADDONS:PFR:NODESTATE TO "Burning".
    ADDONS:PFR:BEGINBURN().
    UNTIL remainingDV <= 0.25 OR ADDONS:PFR:MISSIONHOLD OR NOT ADDONS:PFR:NODEQUEUED OR TIME:SECONDS > burnDeadline {
        SET ADDONS:PFR:PILOTREADY TO TRUE.
        LOCAL updatedVector IS ADDONS:PFR:NODEVECTOR.
        IF NOT ADDONS:PFR:NODEQUEUED { BREAK. }
        SET remainingDV TO updatedVector:MAG.
        IF remainingDV <= 0.25 { BREAK. }
        IF SHIP:AVAILABLETHRUST < 0.001 OR remainingDV > lowestDV+MAX(1,initialDV*0.1) {
            PRINT "Burn stopped: thrust lost or residual increasing.". BREAK.
        }
        SET lowestDV TO MIN(lowestDV,remainingDV).
        SET burnVector TO updatedVector.
        SET acceleration TO SHIP:AVAILABLETHRUST/SHIP:MASS.
        IF VANG(SHIP:FACING:VECTOR,burnVector) > 10 { SET command TO 0. }
        ELSE { SET command TO MIN(1,MAX(0.02,remainingDV/MAX(0.001,acceleration*1.5))). }
        WAIT 0.1.
    }
    stopBurn().
    PRINT ADDONS:PFR:FINISHNODE(remainingDV <= 0.25).
    PRINT "Maneuver ended. Residual: "+ROUND(remainingDV,2)+" m/s.".
}
}
mission().
