// Restart the saved mission pilot after CPU reboot or save/load.
@LAZYGLOBAL OFF.
WAIT UNTIL ADDONS:PFR:AVAILABLE.
IF ADDONS:PFR:RECOVERYPILOT {
    RUNPATH("0:/pf_mission_pilot.ks",ADDONS:PFR:PILOTENGINETAG).
} ELSE {
    PRINT "No saved mission pilot is registered on this CPU.".
}
