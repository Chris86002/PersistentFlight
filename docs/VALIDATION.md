# Validation for 0.25 Experimental

The distributed runtime files are identical to the tested 0.25 package, as recorded by `payload-manifest.json`. The GitHub release packaging adds repository documentation and licensing and removes private fixture-dependent tooling; it does not change mod DLLs or runtime scripts.

- 227 circular transfer math checks, 65 original orbital goal checks, 29 template checks, and 8 frame checks passed.
- Nine native map cases covered higher, lower, and intermediate circular targets, the original four goals, and two single-burn reductions. The tested node solutions were within 5 m of their fixture targets.
- A two-burn, unfocused raising mission survived a cold reload and coast time warp. Recorded residuals were about 0.235 and 0.234 m/s; final apsides were within the trial's 2 km tolerance.
- Existing flight, event, debrief, and analytic regression trials passed; 78 installer checks passed.

These are fixture results, not guaranteed accuracy for every craft, engine, body, mod combination, or burn duration. Other platforms and game versions have not been validated. CI verifies selected portable unit checks only.

Evidence: [circular validation](../CIRCULAR-VALIDATION.json), [native results](../CIRCULAR-NATIVE-RESULTS.txt), [native debrief](../CIRCULAR-NATIVE-DEBRIEF.csv), [flight regression](../FLIGHT-REGRESSION-RESULTS.txt), and [installer validation](../INSTALLER-VALIDATION.txt).
