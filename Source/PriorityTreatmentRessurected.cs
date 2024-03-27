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
                        Log.Message("could not find original method through ReflectedType or TypeByName("+method.ReflectedType.Name+"), continuing");
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

    public class MapComponent_PriorityTreatment : MapComponent
    {

        public List<Pawn> tendablePawns;

        private int ticks;

        public MapComponent_PriorityTreatment(Map map) : base(map)
        {
            this.tendablePawns = new List<Pawn>();

            this.ticks = 0;
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (this.ticks >=300)
            {
                TKS_PriorityTreatment.DebugMessage("MapComponentTick: checking for treatable pawns");


                //cleanup in case other pawns healed or removed
                List<Pawn> removeThese = new List<Pawn>();
                foreach (Pawn sickPawn in this.tendablePawns)
                {
                    if (!TKS_PriorityTreatment.HasTendableHediff(sickPawn))
                    {
                        removeThese.Add(sickPawn);
                    }
                }

                foreach (Pawn healthyPawn in removeThese)
                {
                    this.tendablePawns.Remove(healthyPawn);
                }

                this.tendablePawns = SomeoneNeedsTreatment();

                this.ticks = 0;
            }

            this.ticks += 1;
        }

        private List<Pawn> SomeoneNeedsTreatment()
        {
            List<Pawn> tendablePawns = new List<Pawn>();

            if (TKS_PriorityTreatment.MapHasPatients(this.map))
            {

                foreach (Pawn sickPawn in TKS_PriorityTreatment.PotentialPatientsGlobal(this.map))
                {
                    if (sickPawn == null) { continue; }

                    //TKS_PriorityTreatment.DebugMessage("SomeoneNeedsTreatment: checking " + sickPawn.Name);

                    if (TKS_PriorityTreatment.HasTendableHediff(sickPawn) && (!sickPawn.InAggroMentalState))
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
                               // TKS_PriorityTreatment.DebugMessage("SomeoneNeedsTreatment: checking " + sickPawn.Name + "'s " + hediff.def.defName);
                                if (hediff != null && hediff.TendableNow() && TKS_PriorityTreatment.emergencyHediffs.Contains(hediff.def.defName))
                                {
                                    //TKS_PriorityTreatment.DebugMessage("SomeoneNeedsTreatment: " + hediff.def.defName + " is tendable now, checking pawn settings");
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
                            TKS_PriorityTreatment.DebugMessage("SomeoneNeedsTreatment: " + sickPawn.Name + " needs treatment (include sickness: " + includeSickness.ToString() + ")");
                            if (TKS_PriorityTreatment.GoodLayingStatusForTend(sickPawn, null) || (TKS_PriorityTreatment.CanRescueNowByPlayer(sickPawn) && sickPawn.CarriedBy == null))
                            {
                                if (!tendablePawns.Contains(sickPawn))
                                {
                                    tendablePawns.Add(sickPawn);
                                }
                            }
                        }
                        else
                        {
                            continue;
                        }


                    }
                }
            }
            TKS_PriorityTreatment.DebugMessage("MapComponent: found "+tendablePawns.Count+" pawns to tend on map "+this.map);

            return tendablePawns;
        }

        public override void FinalizeInit()
        {

            base.FinalizeInit();

            this.tendablePawns = SomeoneNeedsTreatment();
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

        //list of hediff names to be considered emergency
        public static List<string> emergencyHediffs = new List<string> {"HeartAttack", "WoundInfection", "Burn", "ChemicalBurn", "Crush", "Cut", "SurgicalCut",
            "Scratch", "Bite", "Stab", "Gunshot", "Shredded", "BeamWound", "InfantIllness", "Crack", "Bruise"};

#if v1_4
        //this fallback list includes included jobDefOfs
        public static List<string> doctorWorkDefs = new List<string> { "Rescue", "TendPatient", "DeliverToBed", "TakeWoundedPrisonerToBed", "TakeDownedPawnToBedDrafted" };
#elif v1_5
        //this fallback list includes included jobDefOfs
        public static List<string> doctorWorkDefs = new List<string> { "Rescue", "TendPatient", "DeliverToBed", "TakeWoundedPrisonerToBed", "TakeDownedPawnToBedDrafted", "TendEntity" };
#endif
        public static IEnumerable<Thing> PotentialPatientsGlobal(Map map)
        {
            List<Pawn> animals = new List<Pawn>();
            //sort between animals and humans
            foreach (Pawn otherPawn in map.mapPawns.SpawnedPawnsWithAnyHediff)
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
        public static IEnumerable<Thing> PotentialPatientsGlobal(Pawn pawn)
        {
            return PotentialPatientsGlobal(pawn.Map);
        }


        public static bool CanRescueNowByPlayer(Pawn patient)
        {
#if v1_5
            return (HealthAIUtility.WantsToBeRescued(patient) && !patient.IsForbidden(Faction.OfPlayer) && !GenAI.EnemyIsNear(patient, 25f, false));
#else
            return (!patient.IsForbidden(Faction.OfPlayer) && patient.Downed && HealthAIUtility.WantsToBeRescuedIfDowned(patient));
#endif
        }

        public static bool MapHasPatients(Map map)
        {
            MapPawns mapPawns = map.mapPawns;

            if (mapPawns.SpawnedPawnsWithAnyHediff.Count >= 1)
            {
                return true;
            }
            return false;
        }

        public static bool MapHasPatients(Pawn pawn)
        {
            return MapHasPatients(pawn.Map);
        }

        public static bool GoodLayingStatusForTend(Pawn patient, Pawn doctor)
        {
            if (doctor != null && patient == doctor)
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
            if (pawn.Dead == true) { return false; }

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

        public static Job  MakePriorityTreatmentJob(Pawn pawn, out Pawn sickPawn, string callingFunction = "TryFindAndStartJob")
        {
            //primary function, return a job for the doctor to do (tend, resuce, or eat) or return null if none
            Job job = null;
            sickPawn = null;

            if (pawn == null) { return job; }

            MapComponent_PriorityTreatment treatmentComponent = pawn.Map?.GetComponent<MapComponent_PriorityTreatment>();

            if (treatmentComponent == null || treatmentComponent.tendablePawns.Count == 0)
            {
                //TKS_PriorityTreatment.DebugMessage("TryFindAndStartJob: pawn "+pawn.Name+" map or component is null, cannot prioritize treatment");
                return job;
            }

            bool allowEating = LoadedModManager.GetMod<TKS_PriorityTreatment>().GetSettings<TKS_PriorityTreatmentSettings>().allowEating;

            Pawn_JobTracker jobTracker = pawn.jobs;

            //break out if there's a queued job (at this stage, the current job is empty but there may be a player-issued job in the queue)
            if (jobTracker.jobQueue != null && jobTracker.jobQueue.AnyPlayerForced)
            {
                TKS_PriorityTreatment.DebugMessage(callingFunction + ": queued jobs by player exist, not overiding");
                return job;
            }

            //dont stop if doing a duty (caravan)
            if (pawn.mindState?.duty != null)
            {
                TKS_PriorityTreatment.DebugMessage(callingFunction + ": not overriding as pawn is doing duty " + pawn.mindState.duty.def.defName);
                return job;
            }

            if (TKS_PriorityTreatment.PawnIsDoctor(pawn, callingFunction))
            {
                //if performing a user-forced job (or already tending), keep going
                if (pawn.CurJob != null)
                {

                    //if eating and we allow docs to eat, keep going
                    if (pawn.CurJob.def == JobDefOf.Ingest && allowEating)
                    {
                        TKS_PriorityTreatment.DebugMessage(callingFunction + ": doctor " + pawn.Name + " is eating but player allows that, not overriding for emergency treatment");
                        return job;
                    }

                    //if we're already tending keep going
                    if (TKS_PriorityTreatment.doctorWorkDefs.Contains(pawn.CurJob.def.defName))
                    {
                        TKS_PriorityTreatment.DebugMessage(callingFunction + ": doctor " + pawn.Name + " is already doing a doctor job");
                        return job;
                    }
                }

                //only go if there are patients
                sickPawn = treatmentComponent.tendablePawns.FirstOrDefault<Pawn>(x => pawn.CanReserveAndReach(x, PathEndMode.OnCell, Danger.Some));

                if (sickPawn != null)
                {
                    if (pawn.CurJob != null)
                    {
                        if (pawn.jobs.curDriver.PlayerInterruptable)
                        {
                            TKS_PriorityTreatment.DebugMessage(callingFunction + ": interrupting current job (" + pawn.CurJobDef.defName + ") and attempting to provide treatment for " + sickPawn.Name);
                            pawn.jobs.curDriver.EndJobWith(JobCondition.InterruptForced);
                        }
                        else
                        {
                            TKS_PriorityTreatment.DebugMessage(callingFunction + ": cannot interrupt job (" + pawn.CurJobDef.defName + ")");
                            return job;

                        }
                    }

                    if (pawn.needs.food != null && pawn.needs.food.CurCategory >= HungerCategory.UrgentlyHungry && allowEating)
                    {
                        TKS_PriorityTreatment.DebugMessage(callingFunction + ": doctor " + pawn.Name + " needs to eat, attempting to create eat job");

                        Thing foodSource;
                        ThingDef foodDef;
#if v1_5
                        if (FoodUtility.TryFindBestFoodSourceFor(pawn, pawn, false, out foodSource, out foodDef, false, true, true, false, false, false, false, false, false, false, false, FoodPreferability.MealTerrible))
#else
                        if (FoodUtility.TryFindBestFoodSourceFor(pawn, pawn, false, out foodSource, out foodDef, false, true, true, false, false, false, false, false, false, false, FoodPreferability.RawTasty))
#endif
                        {
                            Job eatJob = JobMaker.MakeJob(JobDefOf.Ingest, foodSource);

                            return eatJob;
                        }
                    }

                    if (pawn.CanReserveAndReach(sickPawn, PathEndMode.OnCell, Danger.Some))
                    {
                        TKS_PriorityTreatment.DebugMessage(callingFunction + ": pawn " + sickPawn.Name + " needs treatment, attempting to treat with " + pawn.Name);

                        Thing medicine = HealthAIUtility.FindBestMedicine(pawn, sickPawn, false);
                        Job gotoHealJob = new Job(JobDefOf.TendPatient, sickPawn, medicine);
                        gotoHealJob.count = Medicine.GetMedicineCountToFullyHeal(sickPawn);
                        gotoHealJob.draftedTend = false;


                        if (!sickPawn.InBed())
                        {
                            //make rescue job
                            Building_Bed bed = RestUtility.FindPatientBedFor(sickPawn);
                            if (bed != null)
                            {
                                TKS_PriorityTreatment.DebugMessage(callingFunction + ": pawn " + sickPawn.Name + " taking to bed" + pawn.Name);

                                Job rescueJob = JobMaker.MakeJob(JobDefOf.Rescue, sickPawn, bed);
                                rescueJob.count = 1;

                                pawn.Reserve(sickPawn, rescueJob);

                                //bed might already be reserved by Pawn
                                if (pawn.CanReserve(bed))
                                {
                                    pawn.Reserve(bed, rescueJob);
                                }

                                //add tend job to queue
                                pawn.jobs.jobQueue.EnqueueLast(gotoHealJob);

                                return rescueJob;
                            }
                        }

                        pawn.Reserve(sickPawn, gotoHealJob);

                        if (!sickPawn.InBed())
                        {
                            TKS_PriorityTreatment.DebugMessage("TryFindAndStartJob: cannot find bed for " + sickPawn.Name + " attempting to treat on location using draftedTend");
                            gotoHealJob.draftedTend = true;
                        }
                        else
                        {
                            TKS_PriorityTreatment.DebugMessage("TryFindAndStartJob: pawn " + sickPawn.Name + " in bed and ready for treatment");
                        }

                        treatmentComponent.tendablePawns.Remove(sickPawn);
                        return gotoHealJob;
                    }
                }
            }
            return null;
        }

        public static bool PawnIsDoctor(Pawn pawn, string callingFunction = "PawnIsDoctor")
        {
            try
            {
                //do check for all relevant escapes here
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

                //check is assigned to doctoring
                int medPriority = pawn.workSettings.GetPriority(WorkTypeDefOf.Doctor);
                int reqDocPriority = LoadedModManager.GetMod<TKS_PriorityTreatment>().GetSettings<TKS_PriorityTreatmentSettings>().docPriority;

                if (medPriority != 0 && medPriority <= reqDocPriority)
                {
                    if (pawn.health.State == PawnHealthState.Mobile && !pawn.Drafted && !pawn.InMentalState)
                    {
                        TKS_PriorityTreatment.DebugMessage(callingFunction + ": " + pawn.Name + " is an available doctor");
                        return true;

                    }
                }
                return false;
            } catch (Exception ex)
            {
                TKS_PriorityTreatment.DebugMessage(callingFunction+": " + pawn.Name + " exception occured");
                TKS_PriorityTreatment.DebugMessage(callingFunction + ": " + ex.Message);

                return false;
            }

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

            //end if not colonist/player controlled
            if (!(pawn.IsColonistPlayerControlled || pawn.IsColonyMechPlayerControlled))
            {
                return true;
            }

            MapComponent_PriorityTreatment treatmentComponent = pawn.Map?.GetComponent<MapComponent_PriorityTreatment>();

            if (treatmentComponent == null)
            {
                //TKS_PriorityTreatment.DebugMessage("CheckForJobOverride: pawn " + pawn.Name + " map or component is null, cannot prioritize treatment");
                return true;
            }

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

                //only go if there are patients
                Pawn sickPawn = treatmentComponent.tendablePawns.FirstOrDefault<Pawn>(x => x != null);

                if (sickPawn != null ) { 

                    TKS_PriorityTreatment.DebugMessage("CheckForJobOverride: waking up " + pawn.Name + " because " + sickPawn.Name + " needs treatment");

                    pawn.jobs.curDriver.EndJobWith(JobCondition.InterruptForced);

                    //Job gotoHealJob = new Job(JobDefOf.TendPatient, sickPawn);

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

            //end if not colonist/player controlled
            if (!(pawn.IsColonistPlayerControlled || pawn.IsColonyMechPlayerControlled))
            {
                return true;
            }

            Pawn sickPawn = null;

            Job tendJob = TKS_PriorityTreatment.MakePriorityTreatmentJob(pawn, out sickPawn);

            if (tendJob != null)
            {
                MapComponent_PriorityTreatment treatmentComponent = pawn.Map?.GetComponent<MapComponent_PriorityTreatment>();
                treatmentComponent.tendablePawns.Remove(sickPawn);
                __instance.StartJob(tendJob, JobCondition.InterruptOptional, null, false, false, null, null, false, false, null, false, true);
                return false;
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

            //end if not colonist/player controlled
            if (!(__instance.IsColonistPlayerControlled || __instance.IsColonyMechPlayerControlled))
            {
                return true;
            }

            Pawn sickPawn = null;

            Job tendJob = TKS_PriorityTreatment.MakePriorityTreatmentJob(__instance, out sickPawn, "TickRare");

            if (tendJob != null)
            {
                MapComponent_PriorityTreatment treatmentComponent = __instance.Map?.GetComponent<MapComponent_PriorityTreatment>();
                treatmentComponent.tendablePawns.Remove(sickPawn);
                __instance.jobs.StartJob(tendJob, JobCondition.InterruptOptional, null, false, false, null, null, false, false, null, false, true);
                //always run main method here
                return true;
            }
            return true;
        }
    }
}