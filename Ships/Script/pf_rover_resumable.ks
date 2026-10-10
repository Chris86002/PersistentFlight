// Drive one surface waypoint. Select boot/pf_resume.ks for save recovery.
// Start a new route by clearing ADDONS:PFR:CHECKPOINT first.
PARAMETER targetLat, targetLon, cruiseSpeed IS 3, arrivalRadius IS 5.
LOCAL FUNCTION mission {
    WAIT UNTIL ADDONS:PFR:AVAILABLE.
    SAS OFF.
    LOCAL bodyName IS SHIP:BODY:NAME.
    IF ADDONS:PFR:CHECKPOINT <> "" {
        LOCAL saved IS ADDONS:PFR:CHECKPOINT:SPLIT("|").
        IF saved:LENGTH <> 8 OR saved[0] <> "PF8" OR saved[1] <> "ROVER" {
            PRINT "Clear the previous mission checkpoint before starting a rover route.". RETURN.
        }
        IF saved[3] <> bodyName { PRINT "Rover checkpoint belongs to a different body.". BRAKES ON. RETURN. }
        SET targetLat TO saved[4]:TONUMBER.
        SET targetLon TO saved[5]:TONUMBER.
        SET cruiseSpeed TO saved[6]:TONUMBER.
        SET arrivalRadius TO saved[7]:TONUMBER.
    }
    IF ABS(targetLat)>90 OR ABS(targetLon)>180 OR cruiseSpeed<=0 OR cruiseSpeed>5 OR arrivalRadius<2 {
        PRINT "Use valid coordinates, speed up to 5 m/s, and arrival radius at least 2 m.". BRAKES ON. RETURN.
    }
    ADDONS:PFR:AUTOENABLE().
    LOCAL destination IS LATLNG(targetLat,targetLon).
    LOCAL drive IS 0.
    LOCAL phase IS "DRIVE".
    LOCAL nextlog IS 0.
    LOCK WHEELTHROTTLE TO drive.
    LOCK WHEELSTEERING TO destination.
    UNTIL FALSE {
        LOCAL distance IS destination:DISTANCE.
        LOCAL speed IS SHIP:GROUNDSPEED.
        LOCAL forwardSpeed IS VDOT(SHIP:VELOCITY:SURFACE,SHIP:FACING:VECTOR).
        LOCAL desired IS MIN(cruiseSpeed,SQRT(MAX(0,distance-arrivalRadius)*0.6)).
        SET drive TO MAX(0,MIN(0.3,(desired-forwardSpeed)*0.2)).
        IF distance<=arrivalRadius OR forwardSpeed>desired+0.5 {
            SET drive TO 0. BRAKES ON. SET phase TO "BRAKE".
        } ELSE { BRAKES OFF. SET phase TO "DRIVE". }
        IF distance<=arrivalRadius AND speed<0.1 { SET phase TO "ARRIVED". }
        IF ADDONS:PFR:ROVERHOLD { SET drive TO 0. BRAKES ON. SET phase TO "HOLD". }
        IF phase="ARRIVED" OR TIME:SECONDS>=nextlog {
            SET ADDONS:PFR:CHECKPOINT TO "PF8|ROVER|"+phase+"|"+bodyName+"|"+targetLat+"|"+targetLon+"|"+cruiseSpeed+"|"+arrivalRadius.
            LOG TIME:SECONDS+","+phase+","+distance+","+speed+","+drive+","+ADDONS:PFR:WHEELCONTACTS+","+ADDONS:PFR:STEPS+","+SHIP:LATITUDE+","+SHIP:LONGITUDE TO "0:/pf_rover_route.csv".
            SET nextlog TO TIME:SECONDS+1.
        }
        IF phase="ARRIVED" {
            UNLOCK WHEELTHROTTLE. UNLOCK WHEELSTEERING.
            SET SHIP:CONTROL:WHEELTHROTTLE TO 0.
            // Raw kOS steering treats exact zero as no override. Clear the
            // previous turn explicitly before leaving this unfocused rover.
            SET SHIP:CONTROL:WHEELSTEER TO 0.0001. BRAKES ON.
            PRINT "Rover arrived. Checkpoint retained; clear it before the next route.". RETURN.
        }
        WAIT 0.1.
    }
}
mission().
