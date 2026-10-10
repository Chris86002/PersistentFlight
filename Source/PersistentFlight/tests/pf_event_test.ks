SET ADDONS:PFR:PHASE TO "Event handler ready".
UNTIL FALSE {
    ADDONS:PFR:HEARTBEAT().
    IF NOT ADDONS:PFR:MISSIONHOLD {
        LOCAL taskid IS ADDONS:PFR:ACTIONID.
        LOCAL taskname IS ADDONS:PFR:ACTION.
        IF taskid <> "" {
            IF taskname = "ACK_TEST" {
                SET ADDONS:PFR:PHASE TO "Event handler acknowledged task".
                ADDONS:PFR:FINISHACTION(taskid, TRUE).
            } ELSE {
                ADDONS:PFR:FINISHACTION(taskid, FALSE).
            }
        }
    }
    WAIT 0.1.
}
