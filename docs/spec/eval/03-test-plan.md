# Eval Module : Test Plan

**Status:** Built

Checks on the eval tool itself. The real-model accuracy run is scenario 17 of the extraction test plan.

1. Generate twice with the same seed: identical files and answer file. After committing, `git status` shows no change.
2. The set has 30 receipts in seven groups with the specified counts. Every file in the answer file exists and every file is in the answer file.
3. Look at one sample from each group. Each matches its group, and the blurry ones are readable by a person.
4. No generated text contains a real merchant, person, card or account number.
5. Scoring the answer file against itself gives 100%.
6. Corrupt a known number of totals, merchants and dates in a copy: the report shows the matching reduced percentages.
7. Merchant normalization matches case, spacing and punctuation differences but not a different word.
8. Flag coverage matches a hand calculation for 4 wrong totals with 3 flagged.
9. One extractor exception does not stop the run and is listed.
10. Exit code is 0 when all targets pass and non-zero otherwise.
