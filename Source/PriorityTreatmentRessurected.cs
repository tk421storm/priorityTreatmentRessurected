using HarmonyLib;
using System.Reflection;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using HugsLib.Settings;


namespace TKS_PriorityTreatment
{

    [StaticConstructorOnStartup]
    public static class InsertHarmony
    {
        static InsertHarmony()
        {
            Harmony harmony = new Harmony("TKS_PriorityTreatment");
            //Harmony.DEBUG = true;
            harmony.PatchAll();
            // Harmony.DEBUG = false;
            Log.Message($"TKS_PriorityTreatment: Patching finished");
        }
    }

    public class TKS_PriorityTreatmentSettings : ModSettings
    {
        public bool includeSickness = false;
        public bool wakeUpToTend = false;
        public bool allowEating = true;
        public int docPriority = 1;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref includeSickness, "includeSickness");
            Scribe_Values.Look(ref wakeUpToTend, "wakeUpToTend");
            Scribe_Values.Look(ref allowEating, "allowEating");
            Scribe_Values.Look(ref docPriority, "docPriority");

            base.ExposeData();
        }
    }

    public class TKS_PriorityTreatment : Mod
    {
        TKS_PriorityTreatmentSettings settings;

        public static List<string> emergencyHediffs = new List<string> {"HeartAttack", "WoundInfection", "Burn", "ChemicalBurn", "Crush", "Cut", "SurgicalCut",
            "Scratch", "Bite", "Stab", "Gunshot", "Shredded", "BeamWound", "InfantIllness", "Crack", "Bruise"};

        public static IEnumerable<Thing> PotentialPatientsGlobal(Pawn pawn)
        {
            return pawn.Map.mapPawns.SpawnedPawnsWithAnyHediff;
        }

        public static bool MapHasPatients(Pawn pawn)
        {
            MapPawns mapPawns = pawn.Map.mapPawns;

            if (mapPawns.SpawnedPawnsWithAnyHediff.Count >= 1)
            {
                return true;
            }
            return false;
        }

        public static bool GoodLayingStatusForTend(Pawn patient, Pawn doctor)
        {
            if (patient == doctor)
            {
                return true;
            }
            if (patient.RaceProps.Humanlike)
            {
                return patient.InBed();
            }
            return patient.GetPosture() > PawnPosture.Standing;
        }

        public static bool HasTendableHediff(Pawn pawn)
        {
            List<Hediff> pawnsHediffs = pawn.health.hediffSet.hediffs;

            foreach (Hediff hediff in pawnsHediffs)
            {
                if (hediff.TendableNow(true))
                {
                    return true;
                }
            }

            return false;
        }

        public static Pawn SomeoneNeedsTreatment(Pawn doctorPawn)
        {
            if (TKS_PriorityTreatment.MapHasPatients(doctorPawn))
            {

                foreach (Pawn sickPawn in TKS_PriorityTreatment.PotentialPatientsGlobal(doctorPawn))
                {
                    if (sickPawn == null) { continue; }

                    //check for myself
                    if (sickPawn == doctorPawn && !doctorPawn.playerSettings.selfTend) { continue; }

                    if (TKS_PriorityTreatment.HasTendableHediff(sickPawn) && TKS_PriorityTreatment.GoodLayingStatusForTend(sickPawn, doctorPawn) && doctorPawn.CanReserveAndReach(sickPawn, PathEndMode.Touch, Danger.Some) && (!sickPawn.InAggroMentalState))
                    {
                        bool includeSickness = LoadedModManager.GetMod<TKS_PriorityTreatment>().GetSettings<TKS_PriorityTreatmentSettings>().includeSickness;
                        bool shouldTendNow = false;

                        //check whether we should tend this now
                        if (includeSickness)
                        {
                            shouldTendNow = HealthAIUtility.ShouldBeTendedNowByPlayer(sickPawn);
                        }
                        else
                        {
                            HediffSet pawnsHediffs = sickPawn.health.hediffSet;
                            List<Hediff> pawnHediffs = pawnsHediffs.hediffs;

                            //check if hediff is emergency
                            foreach (Hediff hediff in pawnHediffs)
                            {
                                //Log.Message("checking " + otherPawn.Name + "'s " + hediff.def.defName);
                                if (hediff != null && hediff.TendableNow() && TKS_PriorityTreatment.emergencyHediffs.Contains(hediff.def.defName) )
                                {
                                    shouldTendNow = (sickPawn.playerSettings != null && HealthAIUtility.ShouldEverReceiveMedicalCareFromPlayer(sickPawn));
                                    break;

                                }
                            }
                        }
                        if (shouldTendNow)
                        {
                            //Log.Message("SomeoneNeedsTreatment: " + sickPawn.Name + " needs treatment (include sickness: "+includeSickness.ToString()+")");
                            return sickPawn;
                        }
                        
                        
                    }
                }
            }
            return null;
        }

        public static bool PawnIsDoctor(Pawn pawn)
        {
            //do check for all relevant escapes here
            //end if not colonist/player controlled
            if (!(pawn.IsColonistPlayerControlled || pawn.IsColonyMechPlayerControlled))
            {
                return false;
            }

            //end if animal
            if (pawn.HasThingCategory(ThingCategoryDefOf.Animals))
            {
                return false;
            }

            //end if dryad (possible fix to some issues around mods)
            if (ModsConfig.IdeologyActive)
            {
                if (pawn.kindDef == PawnKindDefOf.Dryad_Basic || pawn.kindDef == PawnKindDefOf.Dryad_Gaumaker)
                {
                    return false;
                }
            }

            //not if the pawn is bleeding themselves
            //bool isBleeding = false;
            //HealthAIUtility_Patches.ShouldBeTendedNowByPlayerUrgent_Postfix(pawn, ref isBleeding);

            //if (isBleeding)
            //{
            //    return false;
            //}

            //check is assigned to doctoring
            int medPriority = pawn.workSettings.GetPriority(WorkTypeDefOf.Doctor);
            int reqDocPriority = LoadedModManager.GetMod<TKS_PriorityTreatment>().GetSettings<TKS_PriorityTreatmentSettings>().docPriority;

            if (medPriority != 0 && medPriority <= reqDocPriority)
            {
                if (pawn.health.State == PawnHealthState.Mobile && !pawn.Drafted && !pawn.InMentalState)
                {
                    //if we're already tending keep going
                    if (pawn.CurJobDef == JobDefOf.TendPatient || pawn.CurJobDef == JobDefOf.BeatFire || pawn.CurJobDef == JobDefOf.Rescue)
                    {
                        return false;
                    }
                    else
                    {
                        //Log.Message(pawn.Name + " is a doctor");
                        return true;

                    }
                }
        }
            return false;
        }

        public static bool ShouldBeTendedNowByPlayerUrgent(Pawn pawn)
        {
            //Log.Message("running should be tended now urgent for target pawn: " + pawn.Name);

            HediffSet pawnsHediffs = pawn.health.hediffSet;
            List<Hediff> pawnHediffs = pawnsHediffs.hediffs;

            bool __result = false;

            if (HasTendableHediff(pawn))
            {
                //Log.Message(pawn.Name + " has hediffs needing tend");

                bool includeSickness = LoadedModManager.GetMod<TKS_PriorityTreatment>().GetSettings<TKS_PriorityTreatmentSettings>().includeSickness;

                if (includeSickness)
                {
                    __result = HealthAIUtility.ShouldBeTendedNowByPlayer(pawn);
                }
                else
                {
                    //check if hediff is emergency
                    foreach (Hediff hediff in pawnHediffs)
                    {
                        //Log.Message("checking " + pawn.Name + "'s " + hediff.def.defName);

                        if (hediff != null && TKS_PriorityTreatment.emergencyHediffs.Contains(hediff.def.defName) && hediff.TendableNow(false))
                        {

                            //Log.Message("ShouldBeTendedNowByPlayerUrgent: checking can priority treatment for " + pawn.Name + " due to presence of " + hediff.def.defName + " (include sickness: false)");
                            __result = (pawn.playerSettings != null && HealthAIUtility.ShouldEverReceiveMedicalCareFromPlayer(pawn));
                            break;

                        }
                    }

                }
                if (__result)
                {
                    //Log.Message("ShouldBeTendedNowByPlayerUrgent: Succesfully set priority treatment for target pawn " + pawn.Name);
                }
                else
                {
                    //Log.Message(pawn.Name + " does not need to be treated ");
                }
            }

            return __result;

        }

        public TKS_PriorityTreatment(ModContentPack content) : base(content)
        {
            this.settings = GetSettings<TKS_PriorityTreatmentSettings>();
        }

        private string editBufferFloat;

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);
            listingStandard.CheckboxLabeled("TKSIncludeSickness".Translate(), ref settings.includeSickness, "TKSIncludeSicknessToolTip".Translate());
            listingStandard.CheckboxLabeled("TKSWakeUpToTend".Translate(), ref settings.wakeUpToTend, "TKSWakeUpToTendToolTip".Translate());
            listingStandard.CheckboxLabeled("TKSAllowEating".Translate(), ref settings.allowEating, "TKSAllowEatingToolTip".Translate());

            listingStandard.Label("TKSDocPriorityToolTip".Translate(), -1, "TKSDocPriorityToolTip".Translate());
            listingStandard.TextFieldNumeric<int>(ref settings.docPriority, ref editBufferFloat, 1, 4);
            
            //settings.exampleFloat = listingStandard.Slider(settings.exampleFloat, 100f, 300f);
            listingStandard.End();
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "TKSPriortyTreatmentName".Translate();
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker))]
    static class Pawn_JobTracker_Patches
    {
        [HarmonyPrefix]
        [HarmonyPatch("CheckForJobOverride")]
        public static bool CheckForJobOverride_prefix(ref Pawn_JobTracker __instance)
        {

            // this function is patched because it's a good way to wake up pawns (seems to run slightly less than rare tick)

            bool wakeUpToTEnd = LoadedModManager.GetMod<TKS_PriorityTreatment>().GetSettings<TKS_PriorityTreatmentSettings>().wakeUpToTend;
            Pawn pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();

            //only use this method if we're resting
            if (!(pawn.CurJobDef == JobDefOf.Wait_WithSleeping || pawn.CurJobDef == JobDefOf.Wait_Asleep || pawn.CurJobDef == JobDefOf.LayDownResting || pawn.CurJobDef == JobDefOf.LayDown))
            {
                return true;
            }
            //only use this method if player settings include wake up to tend
            if (!wakeUpToTEnd)
            {
                return true;
            }

            if (TKS_PriorityTreatment.PawnIsDoctor(pawn) && pawn.jobs.curDriver.PlayerInterruptable)
            {
                //Log.Message("CheckForJobOverride: checking if " + pawn.Name + " needs to wake up to tend anyone");

                Pawn sickPawn = TKS_PriorityTreatment.SomeoneNeedsTreatment(pawn);

                if (sickPawn != null ) { 

                    //Log.Message("CheckForJobOverride: waking up " + pawn.Name + " because " + sickPawn.Name + " needs treatment");

                    pawn.jobs.curDriver.EndJobWith(JobCondition.InterruptForced);

                    //Job gotoHealJob = new Job(JobDefOf.TendPatient, sickPawn);

                    //pawn.Reserve(sickPawn, gotoHealJob);
                    //__instance.StartJob(gotoHealJob, JobCondition.InterruptOptional, null, false, false, null, null, false, false, null, false, true);

                    //always perform core actions as well
                    return true;
                }
            }
            return true;
        }
        
        [HarmonyPrefix]
        [HarmonyPatch("TryFindAndStartJob")]
        public static bool TryFindAndStartJob_prefix(ref Pawn_JobTracker __instance)
        {

            //this function is patched because it's this function that returns recreation jobs (and other think jobs that supercede tending)
            //so even if we rareTick interrupt the doctor for a tend job, they wont necessarily take one (if they have needs to fulfill)
            //so we'll check to see if the pawn is a doctor, and then check if we should override the current job with a tend job

            Pawn pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();

            if (pawn == null) { return true; }

            if (TKS_PriorityTreatment.PawnIsDoctor(pawn))
            {
                //if performing a user-forced job (or already tending), keep going
                if (pawn.CurJob != null)
                {
                    if (pawn.CurJob.playerForced || pawn.CurJobDef == JobDefOf.TendPatient || pawn.CurJobDef == JobDefOf.Rescue || pawn.CurJobDef == JobDefOf.OfferHelp)
                    {
                        return true;
                    }

                    bool allowEating = LoadedModManager.GetMod<TKS_PriorityTreatment>().GetSettings<TKS_PriorityTreatmentSettings>().allowEating;

                    //if eating and we allow docs to eat, keep going
                    if (pawn.CurJobDef == JobDefOf.Ingest && allowEating)
                    {
                        //Log.Message("TryFindAndStartJob: doctor " + pawn.Name + " is eating but player allows that, not overriding for emergency treatment");
                        return true;
                    }
                }

                //only go if there are patients
                Pawn sickPawn = TKS_PriorityTreatment.SomeoneNeedsTreatment(pawn);

                if (sickPawn != null)
                {
                    //Log.Message("TryFindAndStartJob: pawn " + sickPawn.Name + " needs treatment, attempting to treat with " + pawn.Name);

                    if (pawn.CurJob != null && pawn.jobs.curDriver.PlayerInterruptable)
                    {
                        //Log.Message("TryFindAndStartJob: interrupting current job (" + pawn.CurJobDef.defName + ") and attempting to provide treatment for " + sickPawn.Name);
                        pawn.jobs.curDriver.EndJobWith(JobCondition.InterruptForced);
                    }

                    Job gotoHealJob = new Job(JobDefOf.TendPatient, sickPawn);

                    pawn.Reserve(sickPawn, gotoHealJob);
                    __instance.StartJob(gotoHealJob, JobCondition.InterruptOptional, null, false, false, null, null, false, false, null, false, true);
                    return false;
                }
                
            }
            return true;

        }
        
    }
    
    [HarmonyPatch(typeof(Pawn), "TickRare")]
    static class Pawn_Patches
    {

        [HarmonyPrefix]
        public static bool TickRare_Prefix(ref Pawn __instance)
        {

            // this is the primary doctor intterupt function - run rarely, check for patients
            // if any can be tended, stop what you're doing and tend them

            if (!TKS_PriorityTreatment.PawnIsDoctor(__instance))
            {
                return true;
            }

            if (!__instance.jobs.curDriver.PlayerInterruptable)
            {
                return true;
            }

            //if performing a user-forced job, keep going
            if (__instance.CurJob.playerForced)
            {
                return true;
            }

            //if we're already tending keep going
            if (__instance.CurJob != null)
            {
                if (__instance.CurJob.playerForced || __instance.CurJobDef == JobDefOf.TendPatient || __instance.CurJobDef == JobDefOf.Rescue || __instance.CurJobDef == JobDefOf.OfferHelp)
                {
                    return true;
                }

                //also ignore if attacking
                if (__instance.CurJobDef == JobDefOf.AttackMelee || __instance.CurJobDef == JobDefOf.AttackStatic)
                {
                    return true;
                }
            }

            //allow eating (if user preferences allows)
            bool allowEat = LoadedModManager.GetMod<TKS_PriorityTreatment>().GetSettings<TKS_PriorityTreatmentSettings>().allowEating;
            if (allowEat && (__instance.CurJobDef == JobDefOf.Ingest))
            {
                //Log.Message("OnRareTick: doctor " + __instance.Name + " is eating but player allows that, not overriding for emergency treatment");
                return true;
            }

            //Log.Message("OnRareTick: pawn " + __instance.Name + " checking for urgent medical needs that can be tended");
            Pawn sickPawn = TKS_PriorityTreatment.SomeoneNeedsTreatment(__instance);

            if (sickPawn != null) { 

                //Log.Message("OnRareTick: sick pawn " + sickPawn.Name + " needs treatment and can be reserved, attempting to stop current job for "+__instance.Name);
                __instance.jobs.curDriver.EndJobWith(JobCondition.InterruptForced);

                //always run main method here
                return true;

            }
            return true;
        }
    }
/*
    [HarmonyPatch(typeof(HealthAIUtility), "ShouldBeTendedNowByPlayerUrgent")]
    static class HealthAIUtility_Patches
    {

        [HarmonyPrefix]
        public static bool ShouldBeTendedNowByPlayerUrgent_Prefix(Pawn pawn, ref bool __result)
        {
            //this is only neccessary if we want doctors to prioritize sickness over their own needs
            bool includeSickness = LoadedModManager.GetMod<TKS_PriorityTreatment>().GetSettings<TKS_PriorityTreatmentSettings>().includeSickness;

            if (!includeSickness)
            {
                __result=TKS_PriorityTreatment.ShouldBeTendedNowByPlayerUrgent(pawn);
                return false;
            }
            else
            {
                Log.Message("ShouldBeTendedNowByPlayerUrgent: Overridding ShouldBeTendedUrgent with standard method (include sickness) for "+pawn.Name);
                __result=HealthAIUtility.ShouldBeTendedNowByPlayer(pawn);
                Log.Message("ShouldBeTendedNowByPlayerUrgent: returns " + __result.ToString());
                return false;
            }
        }
    }
    */
}