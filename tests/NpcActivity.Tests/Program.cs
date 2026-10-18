using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using NpcMonthActivity;

internal static class Program
{
    private static int count;
    private static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception("FAIL: " + name);
        count++; Console.WriteLine("PASS: " + name);
    }
    private static ActivityEngine Engine() { return new ActivityEngine(new ActivityState { PlayerId = "player" }); }
    private static void Near(double a, double b, string label) { Check(Math.Abs(a - b) < 0.00001, label); }
    public static int Main()
    {
        try
        {
            TestTransit();
            TestPillSettlement();
            var materialBag=new Dictionary<string,int> { ["real"]=1,["stack"]=2 };
            Func<string,int> ownedMaterial=s=>materialBag.ContainsKey(s) ? materialBag[s] : 0;
            var materialReservation=new Dictionary<string,int>();
            var emptyMaterials=GrowthMaterialPolicy.OwnedSelection(new[]{null,"","  ","missing"},ownedMaterial,materialReservation);
            Check(emptyMaterials.Count==0 && !LifePolicy.MaterialSelectionComplete(1,emptyMaterials.Count),"native empty slots and stale sole references never display a false 1/1 material count");
            Check(GrowthMaterialPolicy.OwnedSelection(null,ownedMaterial,materialReservation).Count==0,"missing native material selection is empty rather than a full slot");
            var selectedMaterials=GrowthMaterialPolicy.OwnedSelection(new[]{"real","real"},ownedMaterial,materialReservation);
            Check(selectedMaterials.Count==1 && materialBag["real"]==1,"selection counts only owned quantity and does not consume inventory");
            Check(GrowthMaterialPolicy.OwnedSelection(new[]{"real"},ownedMaterial,materialReservation).Count==0,"required and rarity slots share one reservation and cannot overuse the same item");
            Check(GrowthMaterialPolicy.OwnedSelection(new[]{"stack","stack","stack"},ownedMaterial,materialReservation).Count==2,"a real stack can supply only its actual two units");
            Check(GrowthMaterialPolicy.OwnedSelection(new[]{"real"},s=>0,new Dictionary<string,int>()).Count==0,"another actor's sole reference is invalid in this NPC inventory");
            var materialState=new ActivityState { PlayerId="player",RuntimeRevision=9 };
            materialState.Npcs["material"]=new NpcState { Id="material",Budget=3,Plan=new ActivityPlan { Kind=NeedKind.Breakthrough } };
            materialState.UpgradeRuntime();
            Check(materialState.Npcs["material"].Plan==null && materialState.Npcs["material"].Budget==3,"0.3.8 replans unsubmitted major breakthroughs while keeping earned time");
            Check(CultivationPolicy.ActivityDays(true,5)==1 && CultivationPolicy.ActivityDays(true,30)==1,
                "assigned NPC cultivation costs exactly one day regardless of native five-day cycle");
            Check(CultivationPolicy.ActivityDays(false,5)==5 && CultivationPolicy.ActivityDays(false,0)==1,
                "other activity durations retain native cost and minimum guard");
            Check(CultivationPolicy.PreferCultivation(true,500,false) && !CultivationPolicy.PreferCultivation(true,499,false) &&
                !CultivationPolicy.PreferCultivation(false,1000,false) && !CultivationPolicy.PreferCultivation(true,1000,true),
                "prepared pill and living reserve prioritize cultivation only before the actual bottleneck");
            Check(CultivationPolicy.ObsoletePhaseLimit(1,2,1,2,260,260,260,1,1), "Migu phase-one limit is obsolete after native phase two is already attained");
            Check(!CultivationPolicy.ObsoletePhaseLimit(1,2,1,3,1600,260,260,1,2), "next phase lock is retained and never treated as obsolete");
            Check(!CultivationPolicy.ObsoletePhaseLimit(1,2,1,2,260,260,260,1,2), "a limit for the current phase is not removed");
            Check(!CultivationPolicy.ObsoletePhaseLimit(1,2,1,2,260,259,260,1,1) && !CultivationPolicy.ObsoletePhaseLimit(1,2,1,2,260,260,259,1,1), "conflicting or insufficient experience cannot authorize cleanup");
            Check(!CultivationPolicy.ObsoletePhaseLimit(2,2,1,2,260,1600,1600,1,1), "different realm locks are not part of this scoped repair");
            Check(CultivationPolicy.ObsoletePhaseLimit(1,3,1,3,1600,1600,1600,1,2), "phase-two entrance limit is obsolete only after actual phase three");
            Check(!CultivationPolicy.ObsoletePhaseLimit(1,2,1,2,0,260,260,1,1), "unknown entry threshold cannot authorize cleanup");
            Check(!CultivationPolicy.ObsoletePhaseLimit(1,1,1,2,260,260,260,1,1), "full early phase still requires its real pill rather than cleanup");
            Check(CultivationPolicy.PhasePillSucceeded(2,1,3,3), "one real pill consumed and exact next grade reached confirms success");
            Check(!CultivationPolicy.PhasePillSucceeded(1,0,3,2), "consumed pill without advancement is not reported as success");
            Check(!CultivationPolicy.PhasePillSucceeded(1,1,3,3) && !CultivationPolicy.PhasePillSucceeded(2,0,3,3), "missing or multiple consumption is not accepted");
            Check(!CultivationPolicy.PhasePillSucceeded(1,0,3,4) && !CultivationPolicy.PhasePillSucceeded(1,0,0,0), "unexpected or unknown destination never confirms phase success");
            var repairNpc=new NpcState { Id="repair",Budget=2 };
            var repairPlan=new ActivityPlan { Kind=NeedKind.PhaseRepair,ItemId=120,GradeId=2 };
            Check(LifePolicy.ReserveSubmission(repairNpc,repairPlan,1) && repairPlan.Submitted && repairNpc.Budget==1,
                "phase repair reserves NPC time and a durable submission before native mutation");
            repairPlan=JsonConvert.DeserializeObject<ActivityPlan>(JsonConvert.SerializeObject(repairPlan));
            Check(repairPlan.Kind==NeedKind.PhaseRepair && repairPlan.ItemId==120 && !LifePolicy.ReserveSubmission(repairNpc,repairPlan,1),
                "saved phase repair cannot replay the same native submission");
            var phaseUpgrade=new ActivityState { PlayerId="player",RuntimeRevision=7 };
            phaseUpgrade.Npcs["waiting"]=new NpcState { Id="waiting",Budget=2.5,NoGainGradeId=2,NoGainExp=260,
                Plan=new ActivityPlan { Kind=NeedKind.Purchase,ItemId=1011845 },HasTarget=true };
            phaseUpgrade.Npcs["submitted"]=new NpcState { Id="submitted",Plan=new ActivityPlan { Kind=NeedKind.PhasePill,ItemId=1011855,Submitted=true } };
            phaseUpgrade.UpgradeRuntime();
            Check(phaseUpgrade.Npcs["waiting"].Plan==null && !phaseUpgrade.Npcs["waiting"].HasTarget && phaseUpgrade.Npcs["waiting"].Budget==2.5,
                "phase repair discards unsubmitted wrong-stage procurement while preserving earned time");
            Check(phaseUpgrade.Npcs["submitted"].Plan.Submitted && phaseUpgrade.Npcs["waiting"].NoGainExp==260,
                "phase repair preserves submitted native calls and observed cultivation evidence");
            Check(CultivationPolicy.ShouldPreparePill(2,1,false), "phase pill supply does not depend on native full flag");
            Check(!CultivationPolicy.ShouldPreparePill(2,3,true), "one alternative phase pill is enough and prevents duplicate procurement");
            Check(!CultivationPolicy.ShouldPreparePill(3,1,false) && !CultivationPolicy.ShouldPreparePill(1,0,false), "major breakthrough and unknown pill lists do not trigger generic phase purchases");
            Check(CultivationPolicy.AtConfiguredCap(0,260,260,260), "both real exp readings at valid next threshold identify cap");
            Check(!CultivationPolicy.AtConfiguredCap(260,1600,260,260), "being at current stage starting threshold is not a bottleneck");
            Check(!CultivationPolicy.AtConfiguredCap(260,1600,1600,260) && !CultivationPolicy.AtConfiguredCap(260,1600,260,1600), "conflicting raw and dynamic exp cannot force a cap classification");
            Check(!CultivationPolicy.AtConfiguredCap(260,260,500,500) && !CultivationPolicy.AtConfiguredCap(260,0,500,500), "non-increasing or missing config thresholds are not treated as full");
            var noGainNpc=new NpcState();
            Check(!CultivationPolicy.NoGainAtCurrentState(noGainNpc,2,100), "new NPC has no assumed cultivation failure");
            CultivationPolicy.RecordNoGain(noGainNpc,2,100);
            Check(CultivationPolicy.NoGainAtCurrentState(noGainNpc,2,100), "no-gain cultivation is blocked at identical stage and exp");
            Check(!CultivationPolicy.NoGainAtCurrentState(noGainNpc,3,100) && !CultivationPolicy.NoGainAtCurrentState(noGainNpc,2,101), "stage or real exp change permits new evaluation");
            noGainNpc=JsonConvert.DeserializeObject<NpcState>(JsonConvert.SerializeObject(noGainNpc));
            Check(CultivationPolicy.NoGainAtCurrentState(noGainNpc,2,100), "no-gain memory survives save and load");
            Check(!CultivationPolicy.CanCultivate(true,false,false) && !CultivationPolicy.CanCultivate(false,true,false) && !CultivationPolicy.CanCultivate(false,false,true), "both native bottlenecks and observed no-gain stop cultivation");
            Check(NeedPolicy.Urgent(new NeedSnapshot { Hp=1,Health=1,Energy=1,CultivationBlocked=true },new ActivityPlan { Kind=NeedKind.Routine }), "full cultivation interrupts an existing travel plan");
            var nativeMaterials=CultivationPolicy.MaterialIds("5081015_5081025_5081015");
            Check(nativeMaterials.Count==2 && nativeMaterials[0]==5081015 && nativeMaterials[1]==5081025, "native displayed material list supplies distinct concrete IDs");
            Check(CultivationPolicy.MaterialIds("0").Count==0 && CultivationPolicy.MaterialIds(null).Count==0, "empty or zero native material list adds no free material target");
            Check(CultivationPolicy.MaterialIds("-1_invalid_123").Count==1, "unrecognized material expressions are not invented IDs");
            Check(LifePolicy.CanInteract("a", "b", "player", true, true, 1), "assigned actor interacts with ordinary unassigned NPC");
            Check(!LifePolicy.CanInteract("a", "b", "player", false, true, 0), "unassigned NPC cannot initiate");
            Check(!LifePolicy.CanInteract("a", "player", "player", true, true, 0), "automatic social events exclude player");
            Check(!LifePolicy.CanInteract("a", "a", "player", true, true, 0), "NPC cannot interact with itself");
            Check(!LifePolicy.CanInteract("a", "b", "player", true, false, 0), "dead or hidden target is unavailable");
            Check(!LifePolicy.CanInteract("a", "b", "player", true, true, 2), "no remote interaction");
            Check(!LifePolicy.Ready(6, 7) && LifePolicy.Ready(7, 7), "social cooldown uses logical game days");
            Check(!LifePolicy.CanGive(1, false) && !LifePolicy.CanGive(3, true) && LifePolicy.CanGive(2, false), "gift keeps reserve and own upgrade needs");
            Check(!LifePolicy.CanCommission(2,2,2) && LifePolicy.CanCommission(3,2,2), "commission cap resets in new month");
            Check(LifePolicy.MarketKey(2,"town",1) != LifePolicy.MarketKey(3,"town",1), "market quota keyed by month and source");
            Check(!LifePolicy.MaterialSelectionComplete(3,0) && !LifePolicy.MaterialSelectionComplete(3,2), "native allowance cannot bypass missing upgrade materials");
            Check(LifePolicy.MaterialSelectionComplete(3,3) && LifePolicy.MaterialSelectionComplete(0,0), "complete native material selection is permitted");
            var payer=new NpcState { Id="a", Budget=2 }; var request=new ActivityPlan { Kind=NeedKind.Breakthrough };
            Check(!LifePolicy.ReserveSubmission(payer,request,3) && !request.Submitted && payer.Budget==2, "insufficient native time does not submit or charge");
            Check(!LifePolicy.ReserveSubmission(payer,request,double.NaN), "invalid native cost never submits");
            Check(LifePolicy.ReserveSubmission(payer,request,2) && payer.Budget==0, "native time is reserved before invocation");
            payer=JsonConvert.DeserializeObject<NpcState>(JsonConvert.SerializeObject(payer));
            request=JsonConvert.DeserializeObject<ActivityPlan>(JsonConvert.SerializeObject(request)); payer.Budget=3;
            Check(!LifePolicy.ReserveSubmission(payer,request,2) && payer.Budget==3, "restored submitted call cannot charge or execute again");
            var commission = new CommissionState { Token="job", SourceId="town", Home=new Point(0,0), Site=new Point(3,0), Reward=100, AcceptedMonth=1 };
            Check(commission.Valid() && !commission.BeginSettlement(commission.Home), "commission cannot pay before patrol");
            Check(!commission.Patrol(commission.Home) && commission.Patrol(commission.Site), "patrol requires actual target");
            Check(!commission.BeginSettlement(commission.Site), "patrol must return before reward");
            commission = JsonConvert.DeserializeObject<CommissionState>(JsonConvert.SerializeObject(commission));
            Check(commission.BeginSettlement(commission.Home) && !commission.BeginSettlement(commission.Home), "save retains progress and settlement lock prevents duplicate reward");
            var submitted = JsonConvert.DeserializeObject<ActivityPlan>(JsonConvert.SerializeObject(new ActivityPlan { Kind=NeedKind.Ask, Submitted=true, OtherId="b", ItemSoleId="sole" }));
            Check(submitted.Submitted && submitted.OtherId=="b" && submitted.ItemSoleId=="sole", "native submission identity survives save");
            var reservation = new ActivityReservation(3, 1);
            Check(reservation.TryReserve(7, 1), "native step reserves available time");
            Check(reservation.TryReserve(7, 1) && reservation.Spent == 2, "duplicate native prefix does not double-charge");
            Check(!reservation.TryReserve(8, 2) && !reservation.Contains(8), "oversized native step is denied without marking it paid");
            Near(reservation.Collect(), 2, "native startup and step are collected together");
            Near(reservation.Collect(), 0, "native spending is collected only once");
            reservation.Refresh(2);
            Check(reservation.TryReserve(8, 2), "new game time can fund the previously denied step");
            Check(!reservation.TryReserve(9, double.NaN), "invalid native action cost is rejected");
            reservation.ReleaseKey(7); reservation.Refresh(1);
            Check(reservation.TryReserve(7, 1) && reservation.Spent == 5, "reused native action object pays again after its previous call ends");

            var need = new NeedSnapshot { Hp = 1, Health = 1, Energy = 1 };
            var dailyEngine=Engine(); var dailyWorld=new PlannedWorld { TargetDistance=0 };
            dailyWorld.Points["daily"]=new Point(0,0); dailyEngine.Toggle("daily");
            dailyEngine.Advance(.5); dailyEngine.Pump(dailyWorld);
            Check(dailyWorld.Executions==0 && dailyEngine.State.Npcs["daily"].Budget==.5,"half a day cannot execute a full NPC cultivation");
            dailyEngine.Advance(.5); dailyEngine.Pump(dailyWorld);
            Check(dailyWorld.Executions==1 && dailyEngine.State.Npcs["daily"].Budget==0,"one accumulated day funds exactly one cultivation");
            dailyEngine.Pump(dailyWorld);
            Check(dailyWorld.Executions==1,"real-time waiting does not grant extra cultivation");
            dailyEngine.Advance(1); dailyEngine.Pump(dailyWorld);
            Check(dailyWorld.Executions==2 && dailyEngine.State.Npcs["daily"].Budget==0,"next game day cultivates again without a hidden extra rest day");
            Check(NeedPolicy.Select(need, NeedKind.Routine) == NeedKind.Routine, "healthy NPC selects a daily activity");
            need.Hp = .5;
            Check(NeedPolicy.Select(need, NeedKind.Routine) == NeedKind.Recover, "low HP prioritizes recovery");
            need.Hp = .8;
            Check(NeedPolicy.Select(need, NeedKind.Recover) == NeedKind.Recover, "recovery persists through hysteresis band");
            need.Hp = .95; need.Health = .5;
            Check(NeedPolicy.Select(need, NeedKind.Routine) == NeedKind.Recover, "health injury is separate from HP");
            need.Health = 1; need.Energy = .2;
            Check(NeedPolicy.Select(need, NeedKind.Routine) == NeedKind.Rest, "low energy selects rest");
            need.Energy = .6;
            Check(NeedPolicy.Select(need, NeedKind.Rest) == NeedKind.Rest, "rest persists until recovery threshold");
            need.Energy = .9;
            Check(NeedPolicy.Select(need, NeedKind.Rest) == NeedKind.Routine, "recovered NPC resumes daily activity");
            var longRoute = WorldPaths.Find(new Point(0,0), new Point(40,0), p => p.X >= 0 && p.X <= 40 && Math.Abs(p.Y) <= 2);
            Check(longRoute.Count == 40, "world route crosses the old 12-tile radius");
            var worldDetour = WorldPaths.Find(new Point(0,0), new Point(4,0), p => p.X >= 0 && p.X <= 4 && Math.Abs(p.Y) <= 2 && !p.Equals(new Point(2,0)));
            Check(worldDetour.Count == 6, "world route avoids terrain barriers");
            int worldReads = 0;
            var limited = WorldPaths.Find(new Point(0,0), new Point(100,100), p => { worldReads++; return p.X >= 0 && p.Y >= 0; }, 8);
            Check(limited.Count == 0 && worldReads <= 33, "world path search has bounded work");
            var gated = WorldPaths.Find(new Point(0,0), new Point(3,0), p => p.X < 2 && p.Y == 0);
            Check(gated.Count == 0, "ineligible destination is rejected by entry policy");
            var plannedEngine = Engine(); var plannedWorld = new PlannedWorld();
            plannedWorld.Points["n"] = new Point(0,0); plannedEngine.Toggle("n"); plannedEngine.Advance(2);
            plannedEngine.Pump(plannedWorld);
            Check(plannedWorld.Points["n"].X == 2 && plannedWorld.Executions == 0, "travel is not mistaken for completed activity");
            plannedEngine.Advance(1); plannedEngine.Pump(plannedWorld);
            Check(plannedWorld.Executions == 1 && plannedEngine.State.Npcs["n"].Plan == null, "arrival activity pays its own time and completes once");
            plannedEngine.Pump(plannedWorld);
            Check(plannedWorld.Executions == 1, "no repeat completion without new game time");
            var fullEngine=Engine(); var fullWorld=new PlannedWorld(); fullWorld.Points["n"]=new Point(0,0);
            fullEngine.Toggle("n"); fullEngine.Advance(1); fullEngine.Pump(fullWorld);
            fullWorld.Full=true; fullEngine.Advance(1); fullEngine.Pump(fullWorld);
            Check(fullWorld.Points["n"].X==1 && fullWorld.CancelCalls>0 && fullWorld.Executions==1, "NPC becoming full during travel replans in place instead of reaching old cultivation target");
            var busyEngine=Engine(); var busyWorld=new PlannedWorld { BusyId="target" };
            busyWorld.Points["target"]=new Point(0,0); busyEngine.Toggle("target"); busyEngine.Advance(2); busyEngine.Pump(busyWorld);
            Check(busyWorld.Moves==0 && busyEngine.State.Npcs["target"].Budget==2, "passive target cannot start another simultaneous action or lose time");
            var deathEngine=Engine(); var deathWorld=new PlannedWorld { Pending=true };
            deathEngine.Toggle("n"); deathEngine.State.Npcs["n"].Plan=new ActivityPlan(); deathWorld.Reserved=true; deathWorld.Done=true; deathWorld.Blocked=true;
            deathEngine.Pump(deathWorld,1);
            Check(deathWorld.Executions==1 && deathEngine.State.Npcs["n"].Plan==null, "pending native outcome is collected before dead or unavailable position checks");
            var serialPlan = new ActivityPlan { Kind = NeedKind.TownTask, Target = new Point(100,30), SourceId = "town", Reason = "funds", TaskId = "task", RequiredDays = 2 };
            var restoredPlan = JsonConvert.DeserializeObject<ActivityPlan>(JsonConvert.SerializeObject(serialPlan));
            Check(restoredPlan.Target.Equals(serialPlan.Target) && restoredPlan.TaskId == "task" && restoredPlan.Kind == NeedKind.TownTask,
                "persistent plan retains destination and native task identity");
            var urgentEngine = Engine(); var urgentWorld = new PlannedWorld(); urgentWorld.Points["n"] = originForTest();
            urgentEngine.Toggle("n"); urgentEngine.Advance(1); urgentEngine.Pump(urgentWorld);
            urgentWorld.Hp = .2;
            urgentEngine.Advance(1); urgentEngine.Pump(urgentWorld);
            Check(urgentWorld.CancelCalls > 0 && urgentEngine.State.Npcs["n"].Plan.Kind == NeedKind.Recover,
                "new injury interrupts daily travel and replans for recovery");
            var retargetEngine = Engine(); var retargetWorld = new PlannedWorld { RequestTravelOnce = true };
            retargetWorld.Points["n"] = originForTest(); retargetEngine.Toggle("n"); retargetEngine.Advance(3); retargetEngine.Pump(retargetWorld);
            Check(retargetEngine.State.Npcs["n"].Plan.Target.X == 4 && retargetWorld.Points["n"].X == 2 && retargetWorld.Executions == 0,
                "native route request creates paid travel rather than teleporting or completing");
            retargetEngine.Advance(2); retargetEngine.Pump(retargetWorld);
            Check(retargetWorld.Points["n"].X == 4 && retargetWorld.Executions == 0,
                "retargeted trip consumes its travel time before executing");
            var pendingEngine = Engine(); var pendingWorld = new PlannedWorld { Pending = true };
            pendingWorld.Points["n"] = originForTest(); pendingEngine.Toggle("n"); pendingEngine.Advance(3); pendingEngine.Pump(pendingWorld);
            Check(pendingEngine.State.Npcs["n"].Budget == 0 && pendingWorld.Reserved, "pending native action reserves time once");
            pendingWorld.Done = true; pendingEngine.Pump(pendingWorld);
            Check(pendingWorld.Executions == 1 && pendingEngine.State.Npcs["n"].Plan == null,
                "a paid pending action can finish after callback without requiring more game time");

            var origin = new Point(0, 0);
            int gridReads = 0;
            var openPaths = LocalPaths.Search(origin, 6, p => { gridReads++; return true; });
            Check(openPaths.Destinations.Count == 168, "local search stays inside its 13 by 13 region");
            Check(gridReads <= 169 * 4, "local search bounds terrain calls");
            Check(openPaths.PathTo(new Point(2, 0)).Count == 2, "managed search finds shortest cardinal route");
            var blockedPaths = LocalPaths.Search(origin, 3, p => !p.Equals(new Point(1, 0)));
            var detour = blockedPaths.PathTo(new Point(2, 0));
            Check(detour.Count == 4 && !detour.Contains(new Point(1, 0)), "managed route goes around blocked terrain");
            var last = origin;
            bool adjacent = true;
            foreach (var step in detour)
            {
                adjacent &= Math.Abs(step.X - last.X) + Math.Abs(step.Y - last.Y) == 1;
                last = step;
            }
            Check(adjacent && last.Equals(new Point(2, 0)), "every route step is adjacent and reaches destination");
            var isolatedPaths = LocalPaths.Search(origin, 3, p => p.Equals(new Point(1, 1)));
            Check(isolatedPaths.Destinations.Count == 0, "search does not cut diagonal blocked corners");
            Check(openPaths.PathTo(new Point(20, 20)).Count == 0, "out of region destination is unreachable");
            Check(openPaths.PathTo(origin).Count == 0, "current location needs no travel");

            var guard = new ActionGuard();
            guard.Begin(1);
            Check(guard.IsActive(1), "native action guards its current frame");
            guard.End(1);
            Check(!guard.IsActive(1), "postfix releases native action guard");
            for (int frame = 2; frame <= 41; frame++) guard.Begin(frame);
            Check(!guard.IsActive(42), "40 missing completion callbacks cannot block the next frame");
            Check(guard.ReleaseStale(42) == 1 && guard.Depth(42) == 0, "stale native guard is explicitly released");
            guard.Begin(43); guard.Begin(43); guard.End(43);
            Check(guard.IsActive(43), "nested action remains guarded until outer completion");
            guard.End(43);
            Check(!guard.IsActive(43), "nested completion balances");
            guard.Begin(44); guard.End(43);
            Check(guard.IsActive(44), "late old completion cannot release a new action");
            guard.Reset();
            Check(!guard.IsActive(44), "world reset clears action lease");

            var clock = new ActivityClock();
            clock.Reset(100);
            Near(clock.Observe(100), 0, "menu waiting gives no time");
            Near(clock.Observe(101), 1, "integer action time");
            Near(clock.Observe(101), 0, "duplicate observation");
            Near(clock.Observe(90), 0, "rollback gives no future time");
            Near(clock.Observe(91), 1, "rollback resets baseline");
            clock.Reset(29);
            Near(clock.Observe(31), 2, "month boundary uses continuous world day");
            clock.Reset(0);
            double movement = 0;
            for (int i = 0; i < 4; i++) movement += clock.CommitMove(i == 3 ? 1 : 0, .25, true);
            Near(movement, 1, "four fractional moves including integer rollover");
            Near(clock.Observe(1), 0, "move and world clock are not double counted");
            Near(clock.CommitMove(1, .5, false), 0, "rejected movement does not receive its requested cost");
            Near(clock.CommitMove(2, 0, true), 1, "missing movement cost uses actual day change");
            Near(clock.CommitMove(3, double.NaN, true), 1, "invalid cost uses actual day change");

            var engine = Engine(); var world = new FakeWorld();
            world.Points["a"] = new Point(0, 0); world.Points["b"] = new Point(0, 0);
            engine.Advance(10); engine.Toggle("a"); engine.Pump(world);
            Check(world.Points["a"].X == 0, "newly enabled NPC receives no past time");
            engine.Advance(.5); engine.Pump(world);
            Check(world.Points["a"].X == 0, "insufficient movement budget waits");
            engine.Advance(.5); engine.Pump(world);
            Check(world.Points["a"].X == 1, "enabled NPC moves with accrued time");
            Check(world.Points["b"].X == 0, "unassigned NPC is untouched");
            engine.Pump(world);
            Check(world.Points["a"].X == 1, "idle pumping gives no extra movement");
            engine.Advance(1); engine.Pump(world);
            Check(world.Points["a"].X == 2 && engine.State.Npcs["a"].RestRemaining == 1, "arrival begins rest");
            engine.Advance(.5); engine.Pump(world);
            Near(engine.State.Npcs["a"].RestRemaining, .5, "rest consumes game time");
            engine.Toggle("a"); engine.Advance(5); engine.Pump(world);
            Check(world.Points["a"].X == 2, "cancellation stops future movement");

            engine.Toggle("a"); engine.Advance(1); world.Blocked = true; engine.Pump(world);
            Check(world.Points["a"].X == 2 && engine.State.Npcs["a"].Budget == 0, "unavailable NPC pauses without deferred burst");
            world.Blocked = false;
            engine.Advance(1); engine.Pump(world);
            Check(world.Points["a"].X == 3, "available NPC resumes");
            world.Points["a"] = new Point(50, 0); engine.Advance(1); engine.Pump(world);
            Check(world.Points["a"].X == 50 && !engine.State.Npcs["a"].HasTarget, "external relocation invalidates stale target and budget");
            world.NoTarget = true; engine.Advance(1); engine.Pump(world);
            Check(!engine.State.Npcs["a"].HasTarget && engine.State.Npcs["a"].Budget == 0, "unreachable destinations do not accumulate retries");
            world.NoTarget = false; world.RejectMove = true;
            engine.Advance(1); engine.Pump(world);
            Check(world.Points["a"].X == 50 && engine.State.Npcs["a"].Budget == 0, "native move rejection preserves position");
            world.RejectMove = false;

            engine.Advance(.4);
            string saved = JsonConvert.SerializeObject(engine.State);
            var restored = JsonConvert.DeserializeObject<ActivityState>(saved);
            restored.Validate("player");
            Check(restored.Npcs.ContainsKey("a"), "saved enabled list restores");
            Near(restored.Npcs["a"].Budget, .4, "fractional progress restores");
            bool wrongSave = false;
            try { restored.Validate("another-player"); } catch (InvalidOperationException) { wrongSave = true; }
            Check(wrongSave, "cross-save state rejected");
            var old = JsonConvert.DeserializeObject<ActivityState>(saved);
            engine.Advance(8);
            Near(old.Npcs["a"].Budget, .4, "old snapshot cannot inherit future progress");
            restored.Npcs["a"].Budget = double.NaN;
            bool corrupt = false;
            try { restored.Validate("player"); } catch (InvalidOperationException) { corrupt = true; }
            Check(corrupt, "corrupt snapshot rejected");

            var upgrade = new ActivityState { PlayerId = "player" };
            upgrade.Npcs["a"] = new NpcState { Id = "a", Budget = 20, NeededBudget = 1, HasTarget = true };
            upgrade.UpgradeRuntime();
            Check(upgrade.Npcs.ContainsKey("a") && upgrade.Npcs["a"].Budget == 0 && !upgrade.Npcs["a"].HasTarget,
                "upgrade keeps assignments without replaying blocked time");
            upgrade.Npcs["a"].Budget = .5;
            upgrade.UpgradeRuntime();
            Near(upgrade.Npcs["a"].Budget, .5, "runtime upgrade runs only once");
            var previous=new ActivityState { PlayerId="player", RuntimeRevision=4 };
            previous.Npcs["a"]=new NpcState { Id="a",Budget=4,Plan=new ActivityPlan { Kind=NeedKind.Routine } };
            previous.UpgradeRuntime();
            Check(previous.Npcs["a"].Budget==4 && previous.Npcs["a"].Plan==null, "0.3.1 clears stale cultivation route without deleting earned time or assignments");

            engine = Engine(); world = new FakeWorld();
            for (int i = 0; i < 40; i++) { string id = i.ToString(); world.Points[id] = new Point(0, 0); engine.Toggle(id); }
            engine.Advance(100);
            engine.Pump(world, 8);
            Check(world.Moves <= 8, "per-frame operation bound");
            for (int i = 0; i < 20; i++) engine.Pump(world, 8);
            Check(world.Points["39"].X > 0, "round robin prevents starvation");
            double before = engine.State.SimulatedDays;
            engine.Advance(double.PositiveInfinity); engine.Advance(-10); engine.Advance(double.NaN);
            Near(engine.State.SimulatedDays, before, "invalid elapsed time rejected");
            var isolatedEngine = Engine();
            var isolatedWorld = new FakeWorld { ThrowId = "bad" };
            isolatedWorld.Points["bad"] = origin; isolatedWorld.Points["good"] = origin;
            isolatedEngine.Toggle("bad"); isolatedEngine.Toggle("good");
            isolatedEngine.Advance(1); isolatedEngine.Pump(isolatedWorld);
            Check(isolatedWorld.Points["good"].X == 1, "one native adapter error cannot stop other NPCs");
            Check(isolatedEngine.State.Npcs["bad"].Status == "暂停：活动接口异常" && isolatedEngine.State.Npcs["bad"].Budget == 0,
                "adapter failure becomes a visible paused status");
            Console.WriteLine("ALL PASSED: " + count);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static Point originForTest() { return new Point(0, 0); }
    private static void TestPillSettlement()
    {
        var r=new PillUseRecord { ActorId="npc",Token="attempt",SoleId="pill",ItemId=1011855,FromGrade=2,ToGrade=3,
            BeforeCount=1,StartExp=1600,Values=new[]{1011855,1},Stage=PillStage.Deferred,Submitted=true };
        Check(PillPolicy.NeedsInit(false,false,false) && PillPolicy.NeedsInit(true,false,false) &&
            PillPolicy.NeedsInit(true,true,false) && !PillPolicy.NeedsInit(true,true,true),"nested action needs real initialization before isInit can be set");
        Check(PillPolicy.Matches(r,"npc","attempt","pill",1),"escrow matches exact actor, attempt and reserved pill");
        Check(!PillPolicy.Matches(r,"player","attempt","pill",1) && !PillPolicy.Matches(r,"npc","other","pill",1) &&
            !PillPolicy.Matches(r,"npc","attempt","other",1) && !PillPolicy.Matches(r,"npc","attempt","pill",2),"unrelated actors, items, attempts and quantities cannot enter this escrow");
        Check(PillPolicy.Decide(r,2,2,1,true)==PillSettlement.Keep,"unchanged grade retains the original pill");
        Check(PillPolicy.Decide(r,3,3,1,true)==PillSettlement.Commit,"only confirmed target grade permits one actual deduction");
        Check(PillPolicy.Decide(r,3,3,0,false)==PillSettlement.Confirm,"a deduction already present is confirmed without deducting again");
        Check(PillPolicy.Decide(r,2,2,0,false)==PillSettlement.Restore,"failed grade advancement with exactly one pill missing restores that pill");
        Check(PillPolicy.Decide(r,2,3,1,true)==PillSettlement.Hold && PillPolicy.Decide(r,4,4,1,true)==PillSettlement.Hold,
            "conflicting or unexpected grade cannot authorize inventory changes");
        Check(PillPolicy.Decide(r,3,3,1,false)==PillSettlement.Hold && PillPolicy.Decide(r,2,2,2,true)==PillSettlement.Hold,
            "missing reserved sole or unexplained inventory growth stops settlement");
        r.Stage=PillStage.CommitIntent;
        r=JsonConvert.DeserializeObject<PillUseRecord>(JsonConvert.SerializeObject(r));
        Check(PillPolicy.Decide(r,3,3,1,true)==PillSettlement.Commit && PillPolicy.Decide(r,3,3,0,false)==PillSettlement.Confirm,
            "save before and after the deduction recovers exactly once from real counts");
        r.Stage=PillStage.RefundIntent;
        r=JsonConvert.DeserializeObject<PillUseRecord>(JsonConvert.SerializeObject(r));
        Check(PillPolicy.Decide(r,2,2,0,false)==PillSettlement.Restore && PillPolicy.Decide(r,2,2,1,true)==PillSettlement.Keep,
            "save before and after restoration cannot duplicate the refunded pill");
        Check(PillPolicy.Decide(r,3,3,0,false)==PillSettlement.Hold,"grade change during restoration never silently commits");
        r.Stage=PillStage.Released;
        Check(!r.NeedsSettlement && PillPolicy.Decide(r,2,2,1,true)==PillSettlement.Closed,"released attempt is closed without another refund");
        r.Stage=PillStage.Committed;
        Check(!r.NeedsSettlement && PillPolicy.Decide(r,3,3,0,false)==PillSettlement.Closed,"successful attempt cannot charge twice");
        r.Stage=PillStage.Uncertain;
        Check(!r.NeedsSettlement && PillPolicy.Decide(r,2,2,0,false)==PillSettlement.Hold,"uncertain mutation is paused rather than repeatedly replayed");
        r.Stage=PillStage.Prepared; r.Submitted=false;
        Check(!r.NeedsSettlement && PillPolicy.Decide(r,2,2,0,false)==PillSettlement.Hold,"an unsubmitted snapshot cannot grant a refund");
        r.Submitted=true; r.BeforeCount=2;
        Check(PillPolicy.Decide(r,2,2,0,false)==PillSettlement.Hold,"multiple unexplained missing pills never trigger guessed refunds");
        var oldNpc=new NpcState { Id="old",LastEvent="丹药已按原生流程消耗，但瓶颈尚未解除" };
        Check(oldNpc.PillUse==null,"historical 0.3.6 loss does not fabricate a new transaction or compensation");
    }

    private static void TestTransit()
    {
        int id; Point point;
        string reason;
        var source=new PortalRecord { Id="entrance",OpenUi="Build10004",TerrainType=10004,
            FirstPoint=new Point(4,5),Values=new[]{"23","40","0"} };
        Check(TransitPolicy.BuildExit(source,new[]{source},out point,out reason) && point.Equals(new Point(23,40)), "native building exit reads stored x/y without needing event or Chinese portal name");
        source.Values=new[]{"23","40","confirmation-key","pair-a"};
        var peer=new PortalRecord { Id="exit",OpenUi="Build10004",TerrainType=10004,FirstPoint=new Point(28,41),Values=new[]{"4","5","0","pair-a"} };
        Check(TransitPolicy.BuildExit(source,new[]{source,peer},out point,out reason) && point.Equals(peer.FirstPoint), "native pair overrides fallback coordinates using actual peer footprint");
        var unrelated=new PortalRecord { Id="other",TerrainType=10005,FirstPoint=new Point(90,90),Values=new[]{"0","0","0","pair-a"} };
        Check(TransitPolicy.BuildExit(source,new[]{source,unrelated},out point,out reason) && point.Equals(new Point(23,40)), "another building type cannot hijack portal pairing");
        unrelated.TerrainType=10004;
        Check(!TransitPolicy.BuildExit(source,new[]{source,peer,unrelated},out point,out reason), "ambiguous portal peers do not pick an arbitrary exit");
        source=JsonConvert.DeserializeObject<PortalRecord>(JsonConvert.SerializeObject(source));
        Check(TransitPolicy.BuildExit(source,new[]{source,peer},out point,out reason) && point.Equals(peer.FirstPoint), "saved native portal data reconstructs the same real exit");
        source.Values=new[]{"bad","40"};
        Check(!TransitPolicy.BuildExit(source,new[]{source},out point,out reason), "malformed native portal coordinates cannot become an invented destination");
        source.Values=new[]{"23","40"}; source.OpenUi="Build10002";
        Check(!TransitPolicy.BuildExit(source,new[]{source},out point,out reason), "other building UI data cannot be interpreted as portal coordinates");
        Check(LifePolicy.PurchaseFailure(3,0,3).Contains("没有可达路线") && !LifePolicy.PurchaseFailure(3,0,3).Contains("配额"), "unreachable stocked market is distinguished from quota exhaustion");
        Check(LifePolicy.PurchaseFailure(3,3,0).Contains("配额已用完") && !LifePolicy.PurchaseFailure(3,3,0).Contains("没有可达路线"), "quota exhaustion does not falsely claim a route failure");
        Check(LifePolicy.PurchaseFailure(3,1,2).Contains("部分店铺") && LifePolicy.PurchaseFailure(0,0,0).Contains("未列出"), "mixed market failures and no stock remain distinguishable");
        Check(TransitPolicy.CommandId("setPoint_42", "setPoint", out id) && id==42, "native transfer command resolves exact position ID");
        Check(!TransitPolicy.CommandId("setPoint_42_99", "setPoint", out id) && !TransitPolicy.CommandId("setPoint_bad", "setPoint", out id), "unknown argument forms cannot become transfer coordinates");
        Check(TransitPolicy.FixedPoint("12_34",out point) && point.Equals(new Point(12,34)) && !TransitPolicy.FixedPoint("12_34_5",out point), "fixed exit uses exact configured coordinate pair");
        Check(TransitPolicy.Ids("1|2").Count==2 && TransitPolicy.Ids("1|unknown").Count==0, "malformed dialogue references do not yield a partial destination list");
        Func<Point,bool> terrain = p => p.Y==0 && (p.X==0 || p.X==1 || p.X==10 || p.X==11);
        var link = new TransitLink { Entrance=new Point(1,0), Exit=new Point(10,0), SourceId="actual-event" };
        Check(WorldPaths.Find(new Point(0,0),new Point(11,0),terrain).Count==0, "mountain ring stays impassable without a real link");
        var route=WorldPaths.Find(new Point(0,0),new Point(11,0),terrain,new[]{link});
        Check(route.Count==3 && route[0].Equals(link.Entrance) && route[1].Equals(link.Exit), "route walks to entrance then uses actual exit before final destination");
        Check(WorldPaths.Find(new Point(11,0),new Point(0,0),terrain,new[]{link}).Count==0, "one-way formation does not invent a return link");
        Check(WorldPaths.Find(new Point(0,0),new Point(11,0),p=>terrain(p)&&p.X!=10,new[]{link}).Count==0, "invalid exit cannot bypass terrain policy");
        var shortcut=WorldPaths.Find(new Point(0,0),new Point(10,0),p=>p.X>=0&&p.X<=10&&p.Y>=0&&p.Y<=1,
            new[]{new TransitLink { Entrance=new Point(0,1),Exit=new Point(10,0) }});
        Check(shortcut.Count==2 && shortcut[0].Equals(new Point(0,1)), "portal search chooses cheaper walking even when initially moving away from target");
        var engine=Engine(); var world=new TransitWorld(); engine.Toggle("a");
        world.Points["a"]=new Point(0,0); world.Points["b"]=new Point(0,0);
        engine.Pump(world);
        Check(world.Transfers==0 && world.Points["a"].X==0, "time does not start for an idle assigned NPC without player progress");
        engine.Advance(1); engine.Pump(world);
        Check(world.Points["a"].X==10 && world.Transfers==1, "arrival with last walking time still triggers free transfer");
        Near(engine.State.Npcs["a"].Budget,0,"formation adds no time charge or credit");
        Check(world.Points["b"].X==0 && engine.State.Npcs["a"].Plan.Target.X==11, "unassigned NPC and original goal remain unchanged");
        var saved=JsonConvert.DeserializeObject<ActivityState>(JsonConvert.SerializeObject(engine.State)); saved.Validate("player");
        engine=new ActivityEngine(saved); engine.Advance(1); engine.Pump(world);
        Check(world.Points["a"].X==11 && world.Transfers==1 && engine.State.Npcs["a"].Plan!=null, "read after transfer continues walking without repeating formation or losing plan");
        engine.Advance(1); engine.Pump(world);
        Check(world.Executions==1, "activity still executes at final destination after transfer");
        engine=Engine(); world=new TransitWorld { RejectTransfer=true }; engine.Toggle("a"); world.Points["a"]=new Point(0,0);
        engine.Advance(1); engine.Pump(world);
        Check(world.Points["a"].X==1 && world.Transfers==0 && engine.State.Npcs["a"].Status.Contains("出口"), "invalidated formation leaves NPC at entrance and reports failure");
        Near(engine.State.Npcs["a"].Budget,0,"rejected formation does not charge additional time");
    }

    private sealed class TransitWorld : IActivityWorld, IPlannedWorld, ITransitWorld
    {
        public Dictionary<string,Point> Points=new Dictionary<string,Point>();
        public int Transfers,Executions; public bool RejectTransfer;
        public bool TryPosition(string id,out Point p,out string reason) { reason=""; return Points.TryGetValue(id,out p); }
        public bool TryTarget(string id,Point origin,int decision,out Point target) { target=new Point(11,0); return true; }
        public bool TryStep(string id,Point origin,Point target,out Point next,out double cost) { next=new Point(origin.X+1,0); cost=1; return true; }
        public bool Move(string id,Point origin,Point next) { Points[id]=next; return true; }
        public void Trace(string message) { }
        public bool HasReadyTransfer(string id,Point origin,Point target) { return origin.X==1 && target.X==11; }
        public bool Transfer(string id,Point origin,Point target,out Point exit,out string detail)
        {
            exit=origin; detail="出口已失效"; if(RejectTransfer) return false;
            exit=new Point(10,0); Points[id]=exit; Transfers++; detail="通过阵法"; return true;
        }
        public NeedSnapshot Snapshot(string id) { return new NeedSnapshot { Hp=1,Health=1,Energy=1 }; }
        public ActivityPlan Plan(NpcState npc,Point origin,out string reason) { reason=""; return new ActivityPlan { Kind=NeedKind.Purchase,Label="buy",Target=new Point(11,0) }; }
        public ActivityAttempt Execute(NpcState npc,ActivityPlan plan)
        { if(npc.Budget<1) return ActivityAttempt.Result(AttemptKind.Wait,"waiting"); Executions++; return ActivityAttempt.Result(AttemptKind.Complete,"bought",1); }
        public void Cancel(string id) { }
        public bool HasPending(string id) { return false; }
    }

    private class FakeWorld : IActivityWorld
    {
        public readonly Dictionary<string, Point> Points = new Dictionary<string, Point>();
        public bool Blocked, NoTarget, RejectMove;
        public int Moves;
        public string ThrowId;
        public bool TryPosition(string id, out Point p, out string reason)
        { if (id == ThrowId) throw new InvalidOperationException("simulated native failure"); reason = "blocked"; return Points.TryGetValue(id, out p) && !Blocked; }
        public bool TryTarget(string id, Point p, int decision, out Point target)
        { target = new Point(p.X + 2, p.Y); return !NoTarget; }
        public bool TryStep(string id, Point origin, Point target, out Point next, out double cost)
        { next = new Point(origin.X + 1, origin.Y); cost = 1; return true; }
        public bool Move(string id, Point origin, Point next)
        { if (RejectMove) return false; Points[id] = next; Moves++; return true; }
        public void Trace(string message) { }
    }
    private sealed class PlannedWorld : FakeWorld, IPlannedWorld, IActivityAvailability
    {
        public int TargetDistance=2;
        public string BusyId;
        public bool BusyAsTarget(string id) { return id==BusyId; }
        public int Executions;
        public int CancelCalls;
        public double Hp = 1;
        public bool Full;
        public bool RequestTravelOnce, TravelSent, Pending, Reserved, Done;
        public NeedSnapshot Snapshot(string id) { return new NeedSnapshot { Hp = Hp, Health = 1, Energy = 1, CultivationBlocked=Full }; }
        public ActivityPlan Plan(NpcState npc, Point origin, out string reason)
        { reason = ""; return new ActivityPlan { Kind = Full ? NeedKind.Breakthrough : NeedPolicy.Select(Snapshot(npc.Id), NeedKind.Routine), Label = "activity", Target = Full ? origin : new Point(origin.X + TargetDistance, origin.Y) }; }
        public ActivityAttempt Execute(NpcState npc, ActivityPlan plan)
        {
            if (Pending && Reserved)
            {
                if (!Done) return ActivityAttempt.Result(AttemptKind.Pending, "pending");
                Reserved=false; Pending=false; Executions++; return ActivityAttempt.Result(AttemptKind.Complete, "completed");
            }
            if (npc.Budget < 1) return ActivityAttempt.Result(AttemptKind.Wait, "waiting");
            if (Pending) { Reserved = true; return ActivityAttempt.Result(AttemptKind.Pending, "pending", 1); }
            if (RequestTravelOnce && !TravelSent)
            { TravelSent = true; return new ActivityAttempt { Kind = AttemptKind.Travel, Destination = new Point(4,0), Spent = 1, Detail = "travel" }; }
            Executions++; return ActivityAttempt.Result(AttemptKind.Complete, "completed", 1);
        }
        public void Cancel(string id) { CancelCalls++; }
        public bool HasPending(string id) { return Pending && Reserved; }
    }
}
