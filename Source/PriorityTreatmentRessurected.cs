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
                Log.Message("checking " + method.ReflectedType.Name + "." + method.Name + " for other harmony patches");
                MethodInfo original = method.ReflectedType.GetMethod(method.Name);

                if (original is null)
                {
                    Type containingType = TypeByName(method.ReflectedType.Name);
                    original = containingType?.GetMethod(method.Name);

                    if (original is null)
                    {
                        Log.Message("could not find original method through ReflectedType or TypeByName(" + method.ReflectedType.Name + "), continuing");
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
                            Log.Warning("TKS_PriorityTreatment: found other prefixes for method " + method.Name + ": " + patch.owner);
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

    public static class Utils
    {

        static Utils()
        {
        }

        public static bool ExtraPawnChecks(Pawn sickPawn)
        {
            bool returnValue = true;

            //check for android gene
            if (sickPawn.genes != null && sickPawn.genes.GenesListForReading.Count != 0)
            {
                bool isAndroid = sickPawn.genes.GenesListForReading.Any((Gene x) => x.def.defName == "VREA_SyntheticBody");
                if (isAndroid)
                {
                    TKS_PriorityTreatment.DebugMessage("ExtraPawnChecks: not priority tending android");
                    returnValue = false;

                }
            }

            return returnValue;
        }
    }

    public class MapComponent_PriorityTreatment : MapComponent
    {

        public List<Pawn> tendablePawns;

        //holding platforms whose entity needs tending (only filled when the tend entities setting is on)
        public List<Thing> tendablePlatforms;

        private int ticks;

        public MapComponent_PriorityTreatment(Map map) : base(map)
        {
            this.tendablePawns = new List<Pawn>();
            this.tendablePlatforms = new List<Thing>();

            this.ticks = 0;
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (this.ticks >= 300)
            {
                TKS_PriorityTreatment.DebugMessage("MapComponentTick: checking for treatable pawns");

                //rebuild both lists from scratch, which also drops anyone healed or removed since the last check
                this.tendablePawns = SomeoneNeedsTreatment();
                this.tendablePlatforms = EntitiesNeedTreatment();

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

                        //check whether we should tend this now
                        bool shouldTendNow = TKS_PriorityTreatment.NeedsTreatmentNow(sickPawn, includeSickness);
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
            TKS_PriorityTreatment.DebugMessage("MapComponent: found " + tendablePawns.Count + " pawns to tend on map " + this.map);

            return tendablePawns;
        }

        public override void FinalizeInit()
        {

            base.FinalizeInit();

            this.tendablePawns = SomeoneNeedsTreatment();
            this.tendablePlatforms = EntitiesNeedTreatment();
        }

        private List<Thing> EntitiesNeedTreatment()
        {
            List<Thing> platforms = new List<Thing>();
#if !v1_4
            TKS_PriorityTreatmentSettings settings = LoadedModManager.GetMod<TKS_PriorityTreatment>().GetSettings<TKS_PriorityTreatmentSettings>();

            if (!ModsConfig.AnomalyActive || !settings.tendEntities)
            {
                return platforms;
            }

            foreach (Thing platform in this.map.listerThings.ThingsInGroup(ThingRequestGroup.EntityHolder))
            {
                //same check the vanilla work giver uses (skips entities set to release or execute)
                Pawn entity = WorkGiver_TendEntity.GetTendableEnityFromPotentialPlatform(platform);

                if (entity != null && TKS_PriorityTreatment.NeedsTreatmentNow(entity, settings.includeSickness))
                {
                    TKS_PriorityTreatment.DebugMessage("EntitiesNeedTreatment: " + entity.LabelShort + " on " + platform + " needs treatment");
                    platforms.Add(platform);
                }
            }
#endif
            return platforms;
        }
    }

    public class TKS_PriorityTreatmentSettings : ModSettings
    {
        public bool includeSickness = false;
        public bool wakeUpToTend = true;
        public bool allowEating = true;
        public bool debugPrint = false;
        public bool injuredSurgery = true;
        public bool tendEntities = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref includeSickness, "includeSickness");
            Scribe_Values.Look(ref wakeUpToTend, "wakeUpToTend", true);
            Scribe_Values.Look(ref allowEating, "allowEating");
            //Scribe_Values.Look(ref docPriority, "docPriority");
            Scribe_Values.Look(ref debugPrint, "debugPrint");
            Scribe_Values.Look(ref injuredSurgery, "injuredSurgery");
            Scribe_Values.Look(ref tendEntities, "tendEntities");

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
#else
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
#if v1_4
            return (!patient.IsForbidden(Faction.OfPlayer) && patient.Downed && HealthAIUtility.WantsToBeRescuedIfDowned(patient));
#else
            return (HealthAIUtility.WantsToBeRescued(patient) && !patient.IsForbidden(Faction.OfPlayer) && !GenAI.EnemyIsNear(patient, 25f, false));
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

        public static bool NeedsTreatmentNow(Pawn sickPawn, bool includeSickness)
        {
            if (includeSickness)
            {
                return HealthAIUtility.ShouldBeTendedNowByPlayer(sickPawn);
            }

            if (sickPawn.playerSettings == null || !HealthAIUtility.ShouldEverReceiveMedicalCareFromPlayer(sickPawn))
            {
                return false;
            }

            //check if any hediff is an emergency
            foreach (Hediff hediff in sickPawn.health.hediffSet.hediffs)
            {
                if (hediff != null && hediff.TendableNow() && TKS_PriorityTreatment.emergencyHediffs.Contains(hediff.def.defName))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool HasTendableHediff(Pawn pawn)
        {
            if (pawn.Dead == true) { return false; }

            //do extra mod-based checks
            if (!Utils.ExtraPawnChecks(pawn)) { return false; }

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

        private static readonly WorkGiver_Tend tendWorkGiver = new WorkGiver_TendOther();

        //build the tend job through the vanilla work giver, so mods that patch it (smart medicine sets the medicine count there) still apply
        public static Job MakeTendJob(Pawn doctor, Pawn patient)
        {
            return tendWorkGiver.JobOnThing(doctor, patient, false);
        }

#if !v1_4
        private static readonly WorkGiver_TendEntity tendEntityWorkGiver = new WorkGiver_TendEntity();
#endif

        //find a holding platform this doctor can tend right now, using the vanilla work giver's own checks
        public static Thing FindTendablePlatform(Pawn doctor, MapComponent_PriorityTreatment treatmentComponent)
        {
#if !v1_4
            foreach (Thing platform in treatmentComponent.tendablePlatforms)
            {
                if (!platform.IsForbidden(doctor) && tendEntityWorkGiver.HasJobOnThing(doctor, platform) && doctor.CanReach(platform, PathEndMode.ClosestTouch, Danger.Some))
                {
                    return platform;
                }
            }
#endif
            return null;
        }

        public static Job MakeTendEntityJob(Pawn doctor, Thing platform)
        {
#if v1_4
            return null;
#else
            return tendEntityWorkGiver.JobOnThing(doctor, platform, false);
#endif
        }

        public static Job MakePriorityTreatmentJob(Pawn pawn, out Pawn sickPawn, out Job queuedJob, string callingFunction = "TryFindAndStartJob")
        {
            //primary function, return a job for the doctor to do (tend, resuce, or eat) or return null if none
            Job job = null;
            sickPawn = null;
            queuedJob = null;

            if (pawn == null) { return job; }

            MapComponent_PriorityTreatment treatmentComponent = pawn.Map?.GetComponent<MapComponent_PriorityTreatment>();

            if (treatmentComponent == null || (treatmentComponent.tendablePawns.Count == 0 && treatmentComponent.tendablePlatforms.Count == 0))
            {
                //TKS_PriorityTreatment.DebugMessage("TryFindAndStartJob: pawn "+pawn.Name+" map or component is null, cannot prioritize treatment");
                //Log.WarningOnce("PriorityTreatment: map component for this map (or the map itself) is null, no priority treatment will occur", 1);
                return job;
            }

            bool allowEating = LoadedModManager.GetMod<TKS_PriorityTreatment>().GetSettings<TKS_PriorityTreatmentSettings>().allowEating;

            Pawn_JobTracker jobTracker = pawn.jobs;

            //break out if there's a queued job (at this stage, the current job is empty but there may be a player-issued job in the queue)
            if (jobTracker.jobQueue != null)
            {
                if (jobTracker.jobQueue.AnyPlayerForced)
                {
                    TKS_PriorityTreatment.DebugMessage(callingFunction + ": queued jobs by player exist, not overiding");
                    return job;
                }
                if (jobTracker.jobQueue.Count != 0)
                {
                    TKS_PriorityTreatment.DebugMessage(callingFunction + ": queued jobs exist, not overiding (possible compatbility with common sense)");
                    return job;
                }
            }

            //dont stop if doing a duty (caravan)
            if (pawn.mindState?.duty != null)
            {
                TKS_PriorityTreatment.DebugMessage(callingFunction + ": not overriding as pawn is doing duty " + pawn.mindState.duty.def.defName);
                return job;
            }

            //if performing a user-forced job (or already tending), keep going
            if (pawn.CurJob != null)
            {

                //allow player forced jobs (queues dont seem to work at this stage)
                if (pawn.CurJob.playerForced)
                {
                    TKS_PriorityTreatment.DebugMessage(callingFunction + ": doctor " + pawn.Name + " is performing player-set job, not overriding for emergency treatment");
                    return job;
                }

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

            //a doctor resting in bed who is already someone else's patient stays put - getting up would break the other doctor's job
            //note: the other doctor only reserves this pawn once their tend job has started, so there is a small window where this pawn's check
            //can run first and they still get up. may need more code here down the line (ie tracking which patients have been assigned a doctor)
            if (pawn.InBed())
            {
                Pawn reserver = pawn.Map.reservationManager.FirstRespectedReserver(pawn, pawn);
                if (reserver != null && reserver != pawn)
                {
                    TKS_PriorityTreatment.DebugMessage(callingFunction + ": doctor " + pawn.Name + " is in bed and reserved by " + reserver.Name + ", not getting up");
                    return job;
                }
            }

            //only go if there are patients - the doctor themself comes first, but only if the player has turned self-tend on for them (the tend job fails otherwise)
            bool canSelfTend = pawn.playerSettings != null && pawn.playerSettings.selfTend;
            if (canSelfTend && treatmentComponent.tendablePawns.Contains(pawn) && pawn.CanReserveAndReach(pawn, PathEndMode.OnCell, Danger.Some))
            {
                sickPawn = pawn;
            }
            else
            {
                sickPawn = treatmentComponent.tendablePawns.FirstOrDefault<Pawn>(x => x != pawn && pawn.CanReserveAndReach(x, PathEndMode.OnCell, Danger.Some));
            }

            //entities come last, only when no pawn needs this doctor
            Thing sickPlatform = null;
            if (sickPawn == null)
            {
                sickPlatform = TKS_PriorityTreatment.FindTendablePlatform(pawn, treatmentComponent);
            }

            if (sickPawn != null || sickPlatform != null)
            {
                //the current job is not ended here - StartJob ends it, and only once we know the new job can start
                if (pawn.CurJob != null && !pawn.jobs.curDriver.PlayerInterruptable)
                {
                    TKS_PriorityTreatment.DebugMessage(callingFunction + ": cannot interrupt job (" + pawn.CurJobDef.defName + ")");
                    return job;
                }

                if (pawn.needs.food != null && pawn.needs.food.CurCategory >= HungerCategory.UrgentlyHungry && allowEating)
                {
                    TKS_PriorityTreatment.DebugMessage(callingFunction + ": doctor " + pawn.Name + " needs to eat, attempting to create eat job");

                    Thing foodSource;
                    ThingDef foodDef;
#if v1_4
                    if (FoodUtility.TryFindBestFoodSourceFor(pawn, pawn, false, out foodSource, out foodDef, false, true, true, false, false, false, false, false, false, false, FoodPreferability.RawTasty))
#else
                    if (FoodUtility.TryFindBestFoodSourceFor(pawn, pawn, false, out foodSource, out foodDef, false, true, true, false, false, false, false, false, false, false, false, FoodPreferability.MealTerrible))                   
#endif
                    {
                        float nutrition = FoodUtility.GetNutrition(pawn, foodSource, foodDef);
                        Job eatJob = JobMaker.MakeJob(JobDefOf.Ingest, foodSource);
                        eatJob.count = FoodUtility.WillIngestStackCountOf(pawn, foodDef, nutrition);

                        return eatJob;
                    }
                }

                if (sickPlatform != null)
                {
                    TKS_PriorityTreatment.DebugMessage(callingFunction + ": entity on " + sickPlatform + " needs treatment, attempting to treat with " + pawn.Name);

                    treatmentComponent.tendablePlatforms.Remove(sickPlatform);
                    return TKS_PriorityTreatment.MakeTendEntityJob(pawn, sickPlatform);
                }

                if (pawn.CanReserveAndReach(sickPawn, PathEndMode.OnCell, Danger.Some))
                {
                    TKS_PriorityTreatment.DebugMessage(callingFunction + ": pawn " + sickPawn.Name + " needs treatment, attempting to treat with " + pawn.Name);

                    Job gotoHealJob = TKS_PriorityTreatment.MakeTendJob(pawn, sickPawn);
                    gotoHealJob.draftedTend = false;

                    if (!sickPawn.InBed())
                    {
                        //make rescue job
                        Building_Bed bed = RestUtility.FindBedFor(sickPawn, pawn, false, false, sickPawn.GuestStatus); ;
                        if (bed != null)
                        {
                            TKS_PriorityTreatment.DebugMessage(callingFunction + ": pawn " + sickPawn.Name + " taking to bed" + pawn.Name);

                            Job rescueJob = JobMaker.MakeJob(JobDefOf.Rescue, sickPawn, bed);
                            rescueJob.count = 1;

                            //add tend job to queue (this doesn't seem to work correctly)
                            //pawn.jobs.jobQueue.EnqueueFirst(gotoHealJob);
                            queuedJob = gotoHealJob;

                            return rescueJob;
                        }
                    }

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
            return null;
        }

        //try the job's own reservations before it is started, so a doctor is only pulled off what they are doing for a job that can begin
        //the reservations are released again straight away - StartJob makes them for real
        public static bool CanStartJob(Pawn pawn, Job job, string callingFunction)
        {
            bool canStart = job.TryMakePreToilReservations(pawn, false);
            pawn.ClearReservationsForJob(job);

            if (!canStart)
            {
                TKS_PriorityTreatment.DebugMessage(callingFunction + ": " + pawn.Name + " cannot reserve what " + job + " needs, leaving them on their current job");
            }

            return canStart;
        }

        //with manual priorities on, doctors must be set to 1; with them off the game reports every enabled work type as 3
        public static int RequiredDocPriority
        {
            get
            {
                return Find.PlaySettings.useWorkPriorities ? 1 : 3;
            }
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
                int reqDocPriority = TKS_PriorityTreatment.RequiredDocPriority;

                if (medPriority != 0 && medPriority <= reqDocPriority)
                {
                    if (pawn.health.State == PawnHealthState.Mobile && !pawn.Drafted && !pawn.InMentalState)
                    {
                        TKS_PriorityTreatment.DebugMessage(callingFunction + ": " + pawn.Name + " is an available doctor");
                        return true;

                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                TKS_PriorityTreatment.DebugMessage(callingFunction + ": " + pawn.Name + " exception occured");
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
            listingStandard.CheckboxLabeled("TKSAllowInjuredSurgery".Translate(), ref settings.injuredSurgery, "TKSAllowInjuredSurgeryTooltip".Translate());
#if !v1_4
            if (ModsConfig.AnomalyActive)
            {
                listingStandard.CheckboxLabeled("TKSTendEntities".Translate(), ref settings.tendEntities, "TKSTendEntitiesToolTip".Translate());
            }
#endif

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

            if (!TKS_PriorityTreatment.PawnIsDoctor(pawn, "TryFindAndStartJob"))
            {
                return true;
            }

            Pawn sickPawn = null;
            Job queuedJob = null;

            Job tendJob = TKS_PriorityTreatment.MakePriorityTreatmentJob(pawn, out sickPawn, out queuedJob);

            if (tendJob != null)
            {
                MapComponent_PriorityTreatment treatmentComponent = pawn.Map?.GetComponent<MapComponent_PriorityTreatment>();
                treatmentComponent.tendablePawns.Remove(sickPawn);

                if (!TKS_PriorityTreatment.CanStartJob(pawn, tendJob, "TryFindAndStartJob"))
                {
                    return true;
                }

                __instance.StartJob(
                    newJob: tendJob,
                    lastJobEndCondition: JobCondition.InterruptForced,
                    jobGiver: null,
                    resumeCurJobAfterwards: false,
                    cancelBusyStances: false,
                    thinkTree: null,
                    tag: null,
                    fromQueue: false,
                    canReturnCurJobToPool: false,
                    keepCarryingThingOverride: null,
                    continueSleeping: false,
                    addToJobsThisTick: true);
                if (queuedJob != null)
                {
                    pawn.jobs.jobQueue.EnqueueFirst(queuedJob);
                }
                TKS_PriorityTreatment.DebugMessage("TryFindAndStartJob: " + pawn.Name + " has " + pawn.jobs.jobQueue.Count + " jobs in their queue");
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

            if (!TKS_PriorityTreatment.PawnIsDoctor(__instance, "TickRare"))
            {
                return true;
            }

            //do sleep wakeup first - a sleeping doctor is left alone unless the player has asked for them to be woken
            if (!__instance.Awake())
            {
                bool wakeUpToTend = LoadedModManager.GetMod<TKS_PriorityTreatment>().GetSettings<TKS_PriorityTreatmentSettings>().wakeUpToTend;
                //only use this method if player settings include wake up to tend
                if (!wakeUpToTend)
                {
                    return true;
                }
            }

            //here if sleeping or performing any other task
            Pawn sickPawn = null;
            Job queuedJob = null;

            Job tendJob = TKS_PriorityTreatment.MakePriorityTreatmentJob(__instance, out sickPawn, out queuedJob, "TickRare");

            if (tendJob != null)
            {
                MapComponent_PriorityTreatment treatmentComponent = __instance.Map?.GetComponent<MapComponent_PriorityTreatment>();
                treatmentComponent.tendablePawns.Remove(sickPawn);

                if (!TKS_PriorityTreatment.CanStartJob(__instance, tendJob, "TickRare"))
                {
                    return true;
                }

                __instance.jobs.StartJob(
                    newJob: tendJob,
                    lastJobEndCondition: JobCondition.InterruptForced,
                    jobGiver: null,
                    resumeCurJobAfterwards: false,
                    cancelBusyStances: false,
                    thinkTree: null,
                    tag: null,
                    fromQueue: false,
                    canReturnCurJobToPool: false,
                    keepCarryingThingOverride: null,
                    continueSleeping: false,
                    addToJobsThisTick: true);
                if (queuedJob != null)
                {
                    __instance.jobs.jobQueue.EnqueueFirst(queuedJob);
                }
                TKS_PriorityTreatment.DebugMessage("TryFindAndStartJob: " + __instance.Name + " has " + __instance.jobs.jobQueue.Count + " jobs in their queue");

                //always run main method here
                return true;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_DoBill))]
    static class WorkGiver_DoBill_patches
    {
        static MethodBase TargetMethod()
        {
            return typeof(WorkGiver_DoBill).GetMethod("StartOrResumeBillJob", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        [HarmonyPrefix]
        public static bool StartOrResumeBillJob_prefix(WorkGiver_DoBill __instance, Pawn pawn, IBillGiver giver, bool forced)
        {
            bool injuredSurgery = LoadedModManager.GetMod<TKS_PriorityTreatment>().GetSettings<TKS_PriorityTreatmentSettings>().injuredSurgery;

            if (injuredSurgery) { return true; }

            for (int i = 0; i < giver.BillStack.Count; i++)
            {
                Bill bill = giver.BillStack[i];
                Bill_Medical bill_Medical;
                if ((bill_Medical = (bill as Bill_Medical)) != null)
                {
                    if (pawn.health.hediffSet.GetNaturallyHealingInjuredParts().Count != 0)
                    {
                        JobFailReason.Is("NotOperatingWithInjuries".Translate(pawn.Name), bill.Label);
                        //__instance.chosenIngThings.Clear();
                        return false;
                    }
                }
            }

            return true;
        }
    }
}