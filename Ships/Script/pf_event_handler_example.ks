// Run on an idle CPU. Do not run alongside another guidance program on that CPU.
// Example task: REPORT_READY. Create the matching rule in PF -> Event missions.
@LAZYGLOBAL OFF.
LOCAL FUNCTION handler {
    IF ADDONS:PFR:CHECKPOINT <> "" AND NOT ADDONS:PFR:CHECKPOINT:STARTSWITH("PFE1|") {
        PRINT "This CPU has another program's checkpoint. Use an idle CPU.". RETURN.
    }
    ADDONS:PFR:AUTOENABLE().
    SET ADDONS:PFR:MONITORED TO TRUE.
    ADDONS:PFR:EVENT("HANDLER_READY").
    UNTIL FALSE {
        ADDONS:PFR:HEARTBEAT().
        IF NOT ADDONS:PFR:MISSIONHOLD {
            LOCAL taskid IS ADDONS:PFR:ACTIONID.
            LOCAL taskname IS ADDONS:PFR:ACTION.
            IF taskid <> "" {
                IF taskname = "REPORT_READY" {
                    // This example is safe to repeat. Real actions must be idempotent.
                    IF ADDONS:PFR:CHECKPOINT <> "PFE1|"+taskid {
                        PRINT "Ready: "+SHIP:NAME.
                        SET ADDONS:PFR:PHASE TO "Ready reported".
                        SET ADDONS:PFR:CHECKPOINT TO "PFE1|"+taskid.
                    }
                    ADDONS:PFR:FINISHACTION(taskid, TRUE).
                } ELSE {
                    PRINT "Unknown event task: "+taskname.
                    ADDONS:PFR:FINISHACTION(taskid, FALSE).
                }
            }
        }
        WAIT 0.1.
    }
}
handler().
