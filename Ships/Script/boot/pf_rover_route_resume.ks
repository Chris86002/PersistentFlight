// Recovery boot for the route stored on this CPU part.
WAIT UNTIL ADDONS:PFR:AVAILABLE.
IF ADDONS:PFR:ROUTEID <> "" { RUNPATH("0:/pf_rover_route.ks"). }
ELSE { PRINT "No valid rover route. Restore a complete save or create a route on an idle CPU.". }
