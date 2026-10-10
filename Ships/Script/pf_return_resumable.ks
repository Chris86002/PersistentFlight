// Coasting vertical-separation return demonstrator with stock legs and its own CPU.
// Run before separation, with the launch site's latitude and longitude.
PARAMETER siteLat, siteLon, rootClearance IS 22.
LOCAL FUNCTION mission {
LOCAL attachedCount IS SHIP:PARTS:LENGTH.
LOCAL resumed IS FALSE.
LOCAL touched IS FALSE.
LOCAL settling IS FALSE.
IF ADDONS:PFR:CHECKPOINT <> "" {
    LOCAL saved IS ADDONS:PFR:CHECKPOINT:SPLIT("|").
    IF saved:LENGTH <> 8 OR saved[0] <> "PF7" OR saved[1] <> "RETURN" { PRINT "Return checkpoint belongs to another program. Clear it explicitly.". RETURN. }
    SET siteLat TO saved[3]:TONUMBER(). SET siteLon TO saved[4]:TONUMBER().
    SET attachedCount TO saved[5]:TONUMBER(). SET touched TO saved[6] = "TRUE".
    SET rootClearance TO saved[7]:TONUMBER().
    SET settling TO saved[2] = "SETTLING".
    SET resumed TO saved[2] <> "ATTACHED".
}
IF NOT resumed { SET ADDONS:PFR:CHECKPOINT TO "PF7|RETURN|ATTACHED|"+siteLat+"|"+siteLon+"|"+attachedCount+"|FALSE|"+rootClearance. }
PRINT ADDONS:PFR:AUTOENABLE.
IF NOT resumed { WAIT UNTIL SHIP:PARTS:LENGTH < attachedCount. }
WAIT UNTIL ADDONS:PFR:ACTIVE OR ADDONS:PFR:FOCUSED.
// Separation can leave the decoupler as vessel root. Use a command part's
// control reference for steering the booster rather than that root's axes.
FOR controlPart IN SHIP:PARTS {
    IF controlPart:HASMODULE("ModuleCommand") { controlPart:CONTROLFROM(). BREAK. }
}
LIST ENGINES IN engines.
// Use each engine's native steering authority.
FOR engine IN engines { engine:ACTIVATE. }
GEAR ON.
LOCAL command IS 0.
LOCAL aim IS UP:VECTOR.
LOCAL started IS TIME:SECONDS.
LOCAL nextlog IS 0.
LOCAL steeringReleased IS touched.
LOCK THROTTLE TO command.
IF NOT touched { LOCK STEERING TO LOOKDIRUP(aim,NORTH:VECTOR). }
UNTIL FALSE {
    LOCAL height IS ADDONS:PFR:HEIGHT.
    LOCAL radial IS UP:VECTOR.
    LOCAL offset IS LATLNG(siteLat,siteLon):POSITION.
    LOCAL horizontal IS offset - VDOT(offset,radial)*radial.
    LOCAL surfaceVelocity IS SHIP:VELOCITY:SURFACE.
    LOCAL lateralSpeed IS surfaceVelocity - VDOT(surfaceVelocity,radial)*radial.
    LOCAL desiredSpeed IS horizontal*0.05.
    IF desiredSpeed:MAG > 36 { SET desiredSpeed TO desiredSpeed:NORMALIZED*36. }
    LOCAL lateral IS (desiredSpeed-lateralSpeed)*0.6.
    IF lateral:MAG > 3 { SET lateral TO lateral:NORMALIZED*3. }
    LOCAL gravity IS SHIP:BODY:MU/(SHIP:BODY:RADIUS+SHIP:ALTITUDE)^2.
    LOCAL braking IS MAX(1,SHIP:AVAILABLETHRUST/SHIP:MASS-gravity).
    LOCAL targetDown IS -MIN(300,MAX(0.6,MIN((height-rootClearance)*0.25,SQRT(MAX(0,2*braking*0.5*(height-rootClearance-4)))))).
    IF height < 200 AND horizontal:MAG > 40 { SET targetDown TO MAX(-2,(80-height)*0.5). }
    LOCAL phase IS "DESCENT".
    IF SHIP:VERTICALSPEED > 0 { SET phase TO "COAST". }
    IF height < 60 { SET phase TO "LANDING". }
    LOCAL vertical IS gravity+1.2*(targetDown-SHIP:VERTICALSPEED).
    IF vertical <= 0 { SET command TO 0. SET aim TO radial. }
    ELSE {
        // Bound the tilt so terminal descent keeps enough vertical thrust.
        IF lateral:MAG > vertical*0.35 { SET lateral TO lateral:NORMALIZED*vertical*0.35. }
        LOCAL acceleration IS radial*vertical+lateral.
        SET aim TO acceleration:NORMALIZED.
        IF SHIP:AVAILABLETHRUST > 0 {
            SET command TO MAX(0,MIN(1,SHIP:MASS*acceleration:MAG/SHIP:AVAILABLETHRUST)).
        } ELSE { SET command TO 0. }
    }
    IF command = 0 AND height >= 60 { SET phase TO "COAST". }
    // Let suspension settle under gravity after the first gentle foot contact.
    // Continued hover thrust can hold only one leg down until fuel runs out.
    IF ADDONS:PFR:WHEELCONTACTS >= 1 AND ABS(SHIP:VERTICALSPEED) < 2 { SET settling TO TRUE. }
    IF settling { SET command TO 0. SET phase TO "SETTLING". }
    IF ADDONS:PFR:WHEELCONTACTS >= 3 AND ABS(SHIP:VERTICALSPEED) < 2 { SET touched TO TRUE. }
    IF touched {
        SET command TO 0. SET phase TO "TOUCHDOWN".
        IF NOT steeringReleased { UNLOCK STEERING. SET steeringReleased TO TRUE. }
    }
    IF TIME:SECONDS >= nextlog {
        SET ADDONS:PFR:CHECKPOINT TO "PF7|RETURN|"+phase+"|"+siteLat+"|"+siteLon+"|"+attachedCount+"|"+touched+"|"+rootClearance.
        LOG TIME:SECONDS+","+phase+","+height+","+horizontal:MAG+","+SHIP:VERTICALSPEED+","+command+","+ADDONS:PFR:WHEELCONTACTS+","+ADDONS:PFR:STEPS TO "0:/pf_rtls_booster.csv".
        SET nextlog TO TIME:SECONDS+1.
    }
    WAIT 0.02.
}








}
mission().

