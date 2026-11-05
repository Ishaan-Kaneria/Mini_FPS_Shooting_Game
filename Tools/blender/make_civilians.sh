#!/usr/bin/env bash
# The town's people, from MPFB (Tools/blender/human_body.py --civilian): ordinary builds and heights, men and women, young and old.
cd "$(dirname "$0")/../.."
OUT=Assets/Characters/Civilians
gen() { nice -n 15 ~/Programs/blender/blender --background --python Tools/blender/human_body.py -- "$OUT" --name "$1" --civilian "$2" --gender "$3" --age "$4" --muscle "$5" --weight "$6" --proportions "$7" --height "$8" --skin "$9" 2>&1 | grep -E '^\[human\] (report|no )|Traceback' ; rm -f "$OUT/$1_front.png" "$OUT/$1_side.png"; }
gen Civ_Man_A   male_casualsuit04,shoes02,short02       1.0 0.45 0.45 0.52 0.60 1.72 middleage_caucasian_male
gen Civ_Man_B   male_casualsuit05,shoes04,short02       1.0 0.70 0.38 0.62 0.50 1.70 old_caucasian_male
gen Civ_Man_C   male_casualsuit03,shoes05,short01       1.0 0.32 0.50 0.44 0.62 1.76 young_caucasian_male
gen Civ_Man_D   male_worksuit01,shoes03,short04         1.0 0.52 0.48 0.58 0.55 1.74 middleage_asian_male
gen Civ_Woman_A female_casualsuit02,shoes01,long01      0.0 0.42 0.38 0.50 0.60 1.62 middleage_caucasian_female
gen Civ_Woman_B female_elegantsuit01,shoes06,ponytail01      0.0 0.60 0.35 0.58 0.55 1.60 old_caucasian_female
