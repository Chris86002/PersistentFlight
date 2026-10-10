LOCAL FUNCTION mission {
// Select as boot script on each stage CPU. Leave it selected after saving.
WAIT UNTIL ADDONS:PFR:AVAILABLE.
IF ADDONS:PFR:CHECKPOINT = "" { PRINT "No recovery checkpoint; start the stage program manually.". RETURN. }
LOCAL saved IS ADDONS:PFR:CHECKPOINT:SPLIT("|").
IF saved:LENGTH >= 2 AND saved[0] = "PF8" AND saved[1] = "ROVER" {
    RUNPATH("0:/pf_rover_resumable.ks",0,0). RETURN.
}
IF saved:LENGTH < 2 OR saved[0] <> "PF7" { PRINT "Unsupported recovery checkpoint.". RETURN. }
IF saved[1] = "RETURN" { RUNPATH("0:/pf_return_resumable.ks",0,0). }
ELSE IF saved[1] = "ASCENT" { RUNPATH("0:/pf_ascent_resumable.ks"). }
ELSE { PRINT "Unknown recovery role.". }



}
mission().
