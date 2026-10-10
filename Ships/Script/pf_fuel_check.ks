// Advisory only. Current activated engines (or mission pilot engine tag).
@LAZYGLOBAL OFF.
PARAMETER plannedDV IS 100, keepDV IS 150.
WAIT UNTIL ADDONS:PFR:AVAILABLE.
SET ADDONS:PFR:FUELRESERVEDV TO keepDV.
LOCAL fits IS ADDONS:PFR:CHECKFUEL(plannedDV).
PRINT ADDONS:PFR:FUELCHECK.
IF ADDONS:PFR:FUELKNOWN {
    PRINT "Estimated vacuum delta-v left: "+ROUND(ADDONS:PFR:FUELLEFTDV,1)+" m/s.".
    PRINT "Fits chosen reserves: "+fits.
} ELSE { PRINT "Review the unavailable estimate before committing to a burn.". }
