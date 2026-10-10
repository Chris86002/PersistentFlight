// Restart-safe upper-stage demonstration. Select boot/pf_resume.ks on its CPU.
PARAMETER separationHeight IS 8000.
LOCAL FUNCTION mission {
LOCAL startAltitude IS SHIP:ALTITUDE.
LOCAL phase IS "BOOST".
IF ADDONS:PFR:CHECKPOINT <> "" {
    LOCAL saved IS ADDONS:PFR:CHECKPOINT:SPLIT("|").
    IF saved:LENGTH <> 5 OR saved[0] <> "PF7" OR saved[1] <> "ASCENT" { PRINT "Ascent checkpoint belongs to another program. Clear it explicitly.". RETURN. }
    SET phase TO saved[2]. SET startAltitude TO saved[3]:TONUMBER(). SET separationHeight TO saved[4]:TONUMBER().
}
LOCAL FUNCTION commit {
    PARAMETER nextPhase.
    SET phase TO nextPhase.
    SET ADDONS:PFR:CHECKPOINT TO "PF7|ASCENT|"+phase+"|"+startAltitude+"|"+separationHeight.
}
PRINT ADDONS:PFR:AUTOENABLE.
commit(phase).
LIST ENGINES IN engines.
IF phase = "BOOST" {
    FOR engine IN engines { IF engine:TAG = "BOOSTER" { engine:ACTIVATE. } }
    LOCK STEERING TO HEADING(90,90). LOCK THROTTLE TO 1.
    WAIT UNTIL SHIP:ALTITUDE-startAltitude > separationHeight.
    commit("SEPARATE").
}
IF phase = "SEPARATE" {
    LOCK THROTTLE TO 0.
    FOR engine IN engines { IF engine:TAG = "BOOSTER" { engine:SHUTDOWN. } }
    FOR part IN SHIP:PARTS { IF part:HASMODULE("ModuleDecouple") { part:GETMODULE("ModuleDecouple"):DOEVENT("Decouple"). } }
    WAIT 0.5. commit("ASCEND").
}
LIST ENGINES IN upperEngines. upperEngines[0]:ACTIVATE.
LOCAL pitch IS 65. LOCAL command IS 0.
LOCK THROTTLE TO command.
IF phase = "ASCEND" {
    LOCK STEERING TO HEADING(90,pitch). SET command TO 1.
    UNTIL SHIP:APOAPSIS > 105000 {
        SET pitch TO MAX(0,MIN(65,75-SHIP:ALTITUDE/1000)). IF SHIP:ALTITUDE < 20000 { SET pitch TO 80. }
        LOG TIME:SECONDS+","+SHIP:ALTITUDE+","+SHIP:VERTICALSPEED+","+SHIP:PARTS:LENGTH+","+command+","+SHIP:APOAPSIS+","+SHIP:PERIAPSIS TO "0:/pf_rtls_upper.csv".
        WAIT 0.5.
    }
    SET command TO 0. commit("COAST").
}
LOCK STEERING TO PROGRADE.
IF phase = "COAST" {
    LOCAL apoRadius IS SHIP:BODY:RADIUS+SHIP:APOAPSIS.
    LOCAL radial IS UP:VECTOR. LOCAL orbitVelocity IS SHIP:VELOCITY:ORBIT.
    LOCAL horizontalVelocity IS orbitVelocity-radial*VDOT(orbitVelocity,radial).
    LOCAL apoSpeed IS horizontalVelocity:MAG*(SHIP:BODY:RADIUS+SHIP:ALTITUDE)/apoRadius.
    LOCAL circularSpeed IS SQRT(SHIP:BODY:MU/apoRadius).
    LOCAL burnDuration IS MAX(0,circularSpeed-apoSpeed)/(SHIP:AVAILABLETHRUST/SHIP:MASS).
    UNTIL ETA:APOAPSIS < burnDuration/2+8 {
        LOG TIME:SECONDS+","+SHIP:ALTITUDE+","+SHIP:VERTICALSPEED+","+SHIP:PARTS:LENGTH+","+command+","+SHIP:APOAPSIS+","+SHIP:PERIAPSIS TO "0:/pf_rtls_upper.csv". WAIT 0.5.
    }
    commit("BURN").
}
IF phase = "BURN" {
    SET command TO 1.
    UNTIL SHIP:PERIAPSIS > 81000 {
        LOG TIME:SECONDS+","+SHIP:ALTITUDE+","+SHIP:VERTICALSPEED+","+SHIP:PARTS:LENGTH+","+command+","+SHIP:APOAPSIS+","+SHIP:PERIAPSIS TO "0:/pf_rtls_upper.csv". WAIT 0.1.
    }
    SET command TO 0. commit("ORBIT").
}
UNTIL FALSE {
    LOG TIME:SECONDS+","+SHIP:ALTITUDE+","+SHIP:VERTICALSPEED+","+SHIP:PARTS:LENGTH+","+command+","+SHIP:APOAPSIS+","+SHIP:PERIAPSIS TO "0:/pf_rtls_upper.csv". WAIT 0.5.
}


}
mission().

