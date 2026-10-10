// Persistent multi-waypoint rover driver. Run on an idle CPU with stock wheels.
@LAZYGLOBAL OFF.
LOCAL FUNCTION driver {
    WAIT UNTIL ADDONS:PFR:AVAILABLE.
    SAS OFF. BRAKES ON.
    SET ADDONS:PFR:MONITORED TO TRUE.
    LOCAL command IS 0.
    LOCAL stopping IS TRUE.
    LOCAL destination IS LATLNG(SHIP:LATITUDE,SHIP:LONGITUDE).
    LOCK WHEELTHROTTLE TO command.
    LOCK WHEELSTEERING TO destination.
    UNTIL FALSE {
        ADDONS:PFR:HEARTBEAT().
        SET ADDONS:PFR:WARPREADY TO FALSE.
        SET stopping TO TRUE.
        IF ADDONS:PFR:ROUTEID <> "" {
            ADDONS:PFR:ROVERRUN().
            IF ADDONS:PFR:ROUTEACTIVE AND NOT ADDONS:PFR:MISSIONHOLD AND NOT ADDONS:PFR:ROVERHOLD {
                SET destination TO ADDONS:PFR:ROUTETARGET.
                LOCAL desired IS ADDONS:PFR:ROUTESPEED.
                IF desired>0 { SET stopping TO FALSE. }
                LOCAL turnError IS ABS(destination:BEARING).
                IF turnError>20 { SET desired TO MIN(desired,1). }
                LOCAL forwardSpeed IS VDOT(SHIP:VELOCITY:SURFACE,SHIP:FACING:VECTOR).
                SET command TO MAX(0,MIN(0.3,(desired-forwardSpeed)*0.2)).
                IF desired <= 0 OR forwardSpeed>desired+0.2 { SET command TO 0. BRAKES ON. }
                ELSE { BRAKES OFF. }
            } ELSE { SET command TO 0. BRAKES ON. }
        } ELSE { SET command TO 0. BRAKES ON. }
        IF stopping {
            // Keep braking after arrival/holds. Clear stale cooked steering input.
            SET SHIP:CONTROL:WHEELTHROTTLE TO 0.
            SET SHIP:CONTROL:WHEELSTEER TO 0.0001.
        } ELSE { SET SHIP:CONTROL:WHEELSTEER TO 0. }
        WAIT 0.1.
    }
}
driver().
