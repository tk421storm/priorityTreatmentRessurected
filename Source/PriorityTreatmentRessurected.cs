using HarmonyLib;
using System;
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
        public static Type TypeByName(string name)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var tt = assembly.GetType(name);
                if (tt != null)
                {
                    return tt;
                }
            }

            return null;
        }

        static InsertHarmony()
        {
            Harmony harmony = new Harmony("TKS_PriorityTreatment");
            //Harmony.DEBUG = true;
            harmony.PatchAll();
            // Harmony.DEBUG = false;
            
            var myPatchedMethods = harmony.GetPatchedMethods();
            bool anyConflict = false;
            foreach (MethodBase method in myPatchedMethods)
            {
                if (method is null) { continue; };
                Log.Message("checking " + method.ReflectedType.Name + "." + method.Name+" for other harmony patches");
                MethodInfo original = method.ReflectedType.GetMethod(method.Name);
                
                if (original is null)
                {
                    Type containingType = TypeByName(method.ReflectedType.Name);
                    original = containingType?.GetMethod(method.Name);

                    if (original is null)
                    {
                        Log.Warning("could not find original method through ReflectedType or TypeByName("+method.ReflectedType.Name+"), continuing");
                    }
                    continue;
                }

                Patches patches = Harmony.GetPatchInfo(original);

                if (!(patches is null))
                {
                    //Log.Message("found patches from " + patches.Owners.ToCommaList());

                    foreach (var patch in patches.Prefixes)
                    {
                        if (patch != null && patch.owner != harmony.Id)
                        {
                            anyConflict = true;
                            Log.Warning("TKS_PriorityTreatment: found other prefixes for method " + method.Name + ": "+patch.owner);
                        }
                    }
                    foreach (var patch in patches.Postfixes)
                    {
                        if (patch != null && patch.owner != harmony.Id)
                        {
                            anyConflict = true;
                            Log.Warning("TKS_PriorityTreatment: found other postfixes for method " + method.Name + ": " + patch.owner);
                        }
                    }
                    foreach (var patch in patches.Transpilers)
                    {
                        if (patch != null && patch.owner != harmony.Id)
                        {
                            anyConflict = true;
                            Log.Warning("TKS_PriorityTreatment: found other transpilers for method " + method.Name + ": " + patch.owner);
                        }
                    }
                }

            }
                if (anyConflict)
            {
                Log.Warning("TKS_PriorityTreatment: harmony conflicts detected, please report mods if things are acting up");
                
            }
            
            Log.Message("TKS_PriorityTreatment: Patching finished");
        }
    }

    public class TKS_PriorityTreatmentSettings : ModSettings
    {
        public bool includeSickness = false;
        public bool wakeUpToTend = false;
        public bool allowEating = true;
        public int docPriority = 1;
        public bool debugPrint = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref includeSickness, "includeSickness");
            Scribe_Values.Look(ref wakeUpToTend, "wakeUpToTend");
            Scribe_Values.Look(ref allowEating, "allowEating");
            //Scribe_Values.Look(ref docPriority, "docPriority");
            Scribe_Values.Look(ref debugPrint, "debugPrint");

            base.ExposeData();
        }
    }

    public class TKS_PriorityTreatment : Mod
    {
        TKS_PriorityTreatmentSettings settings;

        public static void DebugMessage(string message)
        {
            if (LoadedModManager.GetMod<TKS_PriorityTreatment>().GetSettings<TKS_PriorityTreatmentSettings>().debugPrint)
            {
                Log.Message(message);
            }
        }

        public static List<string> emergencyHediffs = new List<string> {"HeartAttack", "WoundInfection", "Burn", "ChemicalBurn", "Crush", "Cut", "SurgicalCut",
            "Scratch", "Bite", "Stab", "Gunshot", "Shredded", "BeamWound", "InfantIllness", "Crack", "Bruise"};

        public static IEnumerable<Thing> PotentialPatientsGlobal(Pawn pawn)
        {
            List<Pawn> animals = new List<Pawn>();
            //sort between animals and humans
            foreach (Pawn otherPawn in pawn.Map.mapPawns.SpawnedPawnsWithAnyHediff)
            {
                if (otherPawn.RaceProps.Humanlike)
                {
                    yield return otherPawn;
                }
                else
                {
                    animals.Add(otherPawn);
                }
            }

            foreach (Pawn animalPawn in animals)
            {
                yield return animalPawn;
            }
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

                    //TKS_PriorityTreatment.DebugMessage("SomeoneNeedsTreatment: checking " + sickPawn.Name);

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
                                TKS_PriorityTreatment.DebugMessage("SomeoneNeedsTreatment: checking " + sickPawn.Name + "'s " + hediff.def.defName);
                                if (hediff != null && hediff.TendableNow() && TKS_PriorityTreatment.emergencyHediffs.Contains(hediff.def.defName) )
                                {
                                    TKS_PriorityTreatment.DebugMessage("SomeoneNeedsTreatment: " + hediff.def.defName + " is tendable now, checking pawn settings");
                                    shouldTendNow = (sickPawn.playerSettings != null && HealthAIUtility.ShouldEverReceiveMedicalCareFromPlayer(sickPawn));
                                    if (shouldTendNow)
                                    {
                                        break;
                                    }

                                }
                            }
                        }
                        if (shouldTendNow)
                        {
                            TKS_PriorityTreatment.DebugMessage("SomeoneNeedsTreatment: " + sickPawn.Name + " needs treatment (include sickness: "+includeSickness.ToString()+")");
                            return sickPawn;
                        } else
                        {
                            continue; 
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
                        TKS_PriorityTreatment.DebugMessage("PawnIsDoctor: "+pawn.Name + " is a doctor");
                        return true;

                    }
                }
        }
            return false;
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

            //listingStandard.Label("TKSDocPriorityToolTip".Translate(), -1, "TKSDocPriorityToolTip".Translate());
            //listingStandard.TextFieldNumeric<int>(ref settings.docPriority, ref editBufferFloat, 1, 4);

            listingStandard.CheckboxLabeled("TKSDebugPrint".Translate(), ref settings.debugPrint);
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
            if (!wakeUpToTEnd || pawn.CurJob.playerForced)
            {
                return true;
            }           
            //only continue if doctor, job is interrruptable and not doing a duty (caravan et al)
            if (TKS_PriorityTreatment.PawnIsDoctor(pawn) && pawn.jobs.curDriver.PlayerInterruptable && pawn.mindState.duty == null)
            {
                TKS_PriorityTreatment.DebugMessage("CheckForJobOverride: checking if " + pawn.Name + " needs to wake up to tend anyone");

                Pawn sickPawn = TKS_PriorityTreatment.SomeoneNeedsTreatment(pawn);

                if (sickPawn != null ) { 

                    TKS_PriorityTreatment.DebugMessage("CheckForJobOverride: waking up " + pawn.Name + " because " + sickPawn.Name + " needs treatment");

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

            bool allowEating = LoadedModManager.GetMod<TKS_PriorityTreatment>().GetSettings<TKS_PriorityTreatmentSettings>().allowEating;

            if (pawn == null) { return true; }

            //break out if there's a queued job (at this stage, the current job is empty but there may be a player-issued job in the queue)
            if (__instance.jobQueue.AnyPlayerForced)
            {
                TKS_PriorityTreatment.DebugMessage("TryFindAndStartJob: queued jobs by player exist, not overiding");
                return true;
            }

            //dont stop if doing a duty (caravan)
            if (pawn.mindState.duty != null)
            {
                TKS_PriorityTreatment.DebugMessage("TryFindAndStartJob: not overriding as pawn is doing duty " + pawn.mindState.duty.def.defName);
            }

            if (TKS_PriorityTreatment.PawnIsDoctor(pawn))
            {
                //if performing a user-forced job (or already tending), keep going
                if (pawn.CurJob != null)
                {
                    if (pawn.CurJob.playerForced || pawn.CurJobDef == JobDefOf.TendPatient || pawn.CurJobDef == JobDefOf.Rescue || pawn.CurJobDef == JobDefOf.OfferHelp)
                    {
                        return true;
                    }

                    //if eating and we allow docs to eat, keep going
                    if (pawn.CurJobDef == JobDefOf.Ingest && allowEating)
                    {
                        TKS_PriorityTreatment.DebugMessage("TryFindAndStartJob: doctor " + pawn.Name + " is eating but player allows that, not overriding for emergency treatment");
                        return true;
                    }
                }

                //only go if there are patients
                Pawn sickPawn = TKS_PriorityTreatment.SomeoneNeedsTreatment(pawn);

                if (sickPawn != null)
                {

                    if (pawn.needs.food != null && pawn.needs.food.CurCategory >= HungerCategory.UrgentlyHungry && allowEating)
                    {
                        TKS_PriorityTreatment.DebugMessage("TryFindAndStartJob: doctor " + pawn.Name + " needs to eat, not overriding for emergency treatment");
                        return true;
                    }

                    TKS_PriorityTreatment.DebugMessage("TryFindAndStartJob: pawn " + sickPawn.Name + " needs treatment, attempting to treat with " + pawn.Name);

                    if (pawn.CurJob != null && pawn.jobs.curDriver.PlayerInterruptable)
                    {
                        TKS_PriorityTreatment.DebugMessage("TryFindAndStartJob: interrupting current job (" + pawn.CurJobDef.defName + ") and attempting to provide treatment for " + sickPawn.Name);
                        pawn.jobs.curDriver.EndJobWith(JobCondition.InterruptForced);
                    }

                    Thing medicine = HealthAIUtility.FindBestMedicine(pawn, sickPawn, false);
                    Job gotoHealJob = new Job(JobDefOf.TendPatient, sickPawn, medicine);

                    gotoHealJob.count = Medicine.GetMedicineCountToFullyHeal(sickPawn);
                    gotoHealJob.draftedTend = false;

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

            //if performing a user-forced job (or a duty like caravan), keep going
            if (__instance.CurJob.playerForced || __instance.mindState.duty != null)
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
                TKS_PriorityTreatment.DebugMessage("OnRareTick: doctor " + __instance.Name + " is eating but player allows that, not overriding for emergency treatment");
                return true;
            }

            TKS_PriorityTreatment.DebugMessage("OnRareTick: pawn " + __instance.Name + " checking for urgent medical needs that can be tended");
            Pawn sickPawn = TKS_PriorityTreatment.SomeoneNeedsTreatment(__instance);

            if (sickPawn != null) { 

                if (__instance.CurJob != null && __instance.jobs.curDriver.PlayerInterruptable)
                {
                    TKS_PriorityTreatment.DebugMessage("OnRareTick: interrupting current job (" + __instance.CurJobDef.defName + ") and attempting to provide treatment for " + sickPawn.Name);
                    __instance.jobs.curDriver.EndJobWith(JobCondition.InterruptForced);
                }

                //create a job here to avoid re-doing the someoneNeedsTreatment call in TryFindAndStartJob
                Thing medicine = HealthAIUtility.FindBestMedicine(__instance, sickPawn, false);
                Job gotoHealJob = new Job(JobDefOf.TendPatient, sickPawn, medicine);

                gotoHealJob.count = Medicine.GetMedicineCountToFullyHeal(sickPawn);
                gotoHealJob.draftedTend = false;

                __instance.Reserve(sickPawn, gotoHealJob);
                __instance.jobs.StartJob(gotoHealJob, JobCondition.InterruptOptional, null, false, false, null, null, false, false, null, false, true);

                //always run main method here
                return true;

            }
            return true;
        }
    }
}