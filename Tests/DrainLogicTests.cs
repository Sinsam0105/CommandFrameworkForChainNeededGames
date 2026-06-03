using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Sinsam.CommandFramework.Tests
{
    /// <summary>
    /// DrainAfterCommandsAsync / ExitCommandAsync / EndIfIdleAsync가 함께 만드는 핵심 불변식 검증.
    ///   (a) after-command는 최상위 command가 끝난(depth 0) 뒤에만 실행된다.
    ///   (b) FIFO 순서로 소진된다.
    ///   (c) _isDraining 재진입 가드로 중첩 드레인 루프가 생기지 않는다(중복 실행 없음).
    ///   (d) SessionEnded는 정확히 1번 발행된다.
    ///
    /// 모든 fake command는 동기로 완료되므로 파이프라인 전체가 동기적으로 끝난다.
    /// 따라서 UniTask는 GetAwaiter().GetResult()로 블로킹해 결과를 관찰한다.
    /// </summary>
    public class DrainLogicTests
    {
        // ------------------------------------------------------------------
        // Fakes
        // ------------------------------------------------------------------

        private sealed class FakeContext : ICommandContext, ICommandSessionCarrier
        {
            public int Counter;
            public bool IsPreview { get; set; }
            public CommandSession CommandSession { get; set; }
            public void ResetContext() { }
            public void SetContext() { }
        }

        /// <summary>
        /// 실행 시 Label을 log에 기록하는 fake command.
        /// AfterCommands는 타입별 CommandEvent에 붙으므로, 서로 다른 after 체인을 만들려면
        /// 아래처럼 타입을 분리해야 한다(같은 타입은 같은 CommandEvent를 공유).
        /// </summary>
        private abstract class LogCommand : Command<FakeContext>
        {
            public List<string> Log;
            public string Label;
            public bool Result = true;
            public Action<FakeContext> OnLogic;

            public override bool Logic()
            {
                Log?.Add(Label);
                OnLogic?.Invoke(Context);
                return Result;
            }
        }

        private sealed class CommandA : LogCommand { }
        private sealed class CommandB : LogCommand { }
        private sealed class CommandC : LogCommand { }
        private sealed class CommandD : LogCommand { }
        private sealed class CommandE : LogCommand { }
        private sealed class ChildCommand : LogCommand { }

        private static CommandEvent<FakeContext> EventOf<TCommand>() where TCommand : Command<FakeContext>
            => CommandEventRegistry.GetOrCreate<TCommand, FakeContext>();

        private static bool RunReal(LogCommand command, CommandSession session)
            => command.Execute(session).GetAwaiter().GetResult();

        private static (bool IsValid, FakeContext Context) RunPreview(LogCommand command, PreviewAfterMode mode)
            => command.AsyncPreview(mode).GetAwaiter().GetResult();

        [SetUp]
        public void SetUp() => CommandEventRegistry.Clear();

        [TearDown]
        public void TearDown() => CommandEventRegistry.Clear();

        // ------------------------------------------------------------------
        // 1. 기본 FIFO 드레인 — A가 [B, C]를 큐잉. 기대: A → B → C, SessionEnded 1회.
        // ------------------------------------------------------------------
        [Test]
        public void BasicFifoDrain_RunsAfterCommandsInOrderAfterTopLevel()
        {
            var log = new List<string>();
            var ctx = new FakeContext();
            var a = new CommandA { Context = ctx, Log = log, Label = "A" };
            var b = new CommandB { Log = log, Label = "B" };
            var c = new CommandC { Log = log, Label = "C" };

            EventOf<CommandA>().AfterCommands += (context, session) =>
            {
                // A의 Logic이 끝나야(depth 0) 비로소 실행되는지 보기 위해, 이 시점엔 아직 B/C가 log에 없어야 한다.
                Assert.AreEqual(new[] { "A" }, log.ToArray());
                b.Context = context;
                c.Context = context;
                return new ICommand[] { b, c };
            };

            int ended = 0;
            var session = new CommandSession();
            session.SessionEndedEvent += _ => ended++;

            bool ok = RunReal(a, session);

            Assert.IsTrue(ok);
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, log);
            Assert.AreEqual(1, ended, "SessionEnded는 정확히 1회여야 한다.");
            Assert.IsTrue(session.IsEnded);
            Assert.AreEqual(0, session.QueuedCommandCount);
        }

        // ------------------------------------------------------------------
        // 2. 체이닝 — B가 다시 [D]를 큐잉. 기대: A → B → C → D 전부 같은 세션에서 소진, SessionEnded 1회.
        //    _isDraining 가드 하에서 바깥 while 루프가 새 항목을 집어가는지 확인하는 핵심 케이스.
        // ------------------------------------------------------------------
        [Test]
        public void ChainedAfterCommands_OuterDrainLoopPicksUpNewlyEnqueued()
        {
            var log = new List<string>();
            var ctx = new FakeContext();
            var a = new CommandA { Context = ctx, Log = log, Label = "A" };
            var b = new CommandB { Log = log, Label = "B" };
            var c = new CommandC { Log = log, Label = "C" };
            var d = new CommandD { Log = log, Label = "D" };

            EventOf<CommandA>().AfterCommands += (context, session) =>
            {
                b.Context = context;
                c.Context = context;
                return new ICommand[] { b, c };
            };
            EventOf<CommandB>().AfterCommands += (context, session) =>
            {
                // B는 드레인 도중 실행되며, 여기서 enqueue한 D는 바깥 루프가 이어받아야 한다.
                d.Context = context;
                return new ICommand[] { d };
            };

            int ended = 0;
            int endedAsync = 0;
            var session = new CommandSession();
            session.SessionEndedEvent += _ => ended++;
            session.SessionEndedAsyncEvent += _ => { endedAsync++; return UniTask.CompletedTask; };

            bool ok = RunReal(a, session);

            Assert.IsTrue(ok);
            CollectionAssert.AreEqual(new[] { "A", "B", "C", "D" }, log);
            Assert.AreEqual(1, ended, "SessionEnded(sync)는 정확히 1회여야 한다.");
            Assert.AreEqual(1, endedAsync, "SessionEnded(async)는 정확히 1회여야 한다.");
            Assert.IsTrue(session.IsEnded);
        }

        // ------------------------------------------------------------------
        // 3. 중첩 커맨드(depth>1) — A의 Logic 안에서 child를 직접 Execute. child의 after(E)는
        //    child가 끝나는 즉시가 아니라 최상위 A가 depth 0으로 빠질 때 드레인돼야 한다.
        //    (중간 depth에서 DrainAfterCommandsAsync가 early-return)
        // ------------------------------------------------------------------
        [Test]
        public void NestedCommand_AfterCommandDrainsOnlyWhenTopLevelExits()
        {
            var log = new List<string>();
            var ctx = new FakeContext();
            var child = new ChildCommand { Log = log, Label = "child" };
            var e = new CommandE { Log = log, Label = "E" };

            List<string> logWhenChildReturned = null;

            var a = new CommandA
            {
                Context = ctx,
                Log = log,
                Label = "A",
                OnLogic = context =>
                {
                    var session = ((ICommandSessionCarrier)context).CommandSession;
                    child.Context = context;
                    child.Execute(session).GetAwaiter().GetResult();
                    // child가 막 끝난 시점(아직 A는 depth 1). child의 after(E)는 아직 실행되면 안 된다.
                    logWhenChildReturned = new List<string>(log);
                }
            };

            EventOf<ChildCommand>().AfterCommands += (context, session) =>
            {
                e.Context = context;
                return new ICommand[] { e };
            };

            int ended = 0;
            int endedAsync = 0;
            var rootSession = new CommandSession();
            rootSession.SessionEndedEvent += _ => ended++;
            rootSession.SessionEndedAsyncEvent += _ => { endedAsync++; return UniTask.CompletedTask; };

            bool ok = RunReal(a, rootSession);

            Assert.IsTrue(ok);
            // child 종료 직후엔 E가 없어야 한다(중간 depth에서 드레인 금지).
            CollectionAssert.AreEqual(new[] { "A", "child" }, logWhenChildReturned);
            // 최종적으로는 최상위 A가 빠질 때 E가 드레인된다.
            CollectionAssert.AreEqual(new[] { "A", "child", "E" }, log);
            Assert.AreEqual(1, ended, "SessionEnded(sync)는 정확히 1회여야 한다.");
            Assert.AreEqual(1, endedAsync, "SessionEnded(async)는 정확히 1회여야 한다.");
        }

        // ------------------------------------------------------------------
        // 4. 재진입 가드 — 드레인 중 실행되는 커맨드가 또 enqueue해도 두 번째 드레인 루프가 시작되지 않고
        //    바깥 루프가 이어받는다. 각 커맨드가 정확히 1번만 실행됐는지(중복 없음)로 검증한다.
        // ------------------------------------------------------------------
        [Test]
        public void ReentrancyGuard_EachCommandRunsExactlyOnce()
        {
            var log = new List<string>();
            var ctx = new FakeContext();
            var a = new CommandA { Context = ctx, Log = log, Label = "A" };
            var b = new CommandB { Log = log, Label = "B" };
            var c = new CommandC { Log = log, Label = "C" };
            var d = new CommandD { Log = log, Label = "D" };
            var e = new CommandE { Log = log, Label = "E" };

            EventOf<CommandA>().AfterCommands += (context, session) => { b.Context = context; return new ICommand[] { b }; };
            EventOf<CommandB>().AfterCommands += (context, session) => { c.Context = context; return new ICommand[] { c }; };
            EventOf<CommandC>().AfterCommands += (context, session) => { d.Context = context; return new ICommand[] { d }; };
            EventOf<CommandD>().AfterCommands += (context, session) => { e.Context = context; return new ICommand[] { e }; };

            int ended = 0;
            var session = new CommandSession();
            session.SessionEndedEvent += _ => ended++;

            bool ok = RunReal(a, session);

            Assert.IsTrue(ok);
            CollectionAssert.AreEqual(new[] { "A", "B", "C", "D", "E" }, log);
            Assert.AreEqual(log.Count, log.Distinct().Count(), "어떤 커맨드도 두 번 실행되면 안 된다.");
            Assert.AreEqual(1, ended, "SessionEnded는 정확히 1회여야 한다.");
        }

        // ------------------------------------------------------------------
        // 6. 종료된 세션에 enqueue → throw.
        // ------------------------------------------------------------------
        [Test]
        public void EnqueueAfterEndedSession_Throws()
        {
            var log = new List<string>();
            var ctx = new FakeContext();
            var a = new CommandA { Context = ctx, Log = log, Label = "A" };

            var session = new CommandSession();
            RunReal(a, session);

            Assert.IsTrue(session.IsEnded);
            Assert.Throws<InvalidOperationException>(() => session.EnqueueAfter(new CommandB()));
        }

        // ------------------------------------------------------------------
        // 7. 실패 시 드레인 안 함 — A.Logic이 false 반환. shouldDrainAfterCommands == false →
        //    after-command 미실행, 큐도 비어 있어야 한다.
        // ------------------------------------------------------------------
        [Test]
        public void FailedCommand_DoesNotDrainAfterCommands()
        {
            var log = new List<string>();
            var ctx = new FakeContext();
            var a = new CommandA { Context = ctx, Log = log, Label = "A", Result = false };
            var b = new CommandB { Log = log, Label = "B" };

            EventOf<CommandA>().AfterCommands += (context, session) => { b.Context = context; return new ICommand[] { b }; };

            var session = new CommandSession();
            bool ok = RunReal(a, session);

            Assert.IsFalse(ok);
            CollectionAssert.AreEqual(new[] { "A" }, log, "after-command B는 실행되면 안 된다.");
            Assert.AreEqual(0, session.QueuedCommandCount);
        }

        // ------------------------------------------------------------------
        // 8. Preview 모드별 분기.
        // ------------------------------------------------------------------
        [Test]
        public void Preview_None_DoesNotCollectAfterCommands()
        {
            var log = new List<string>();
            var ctx = new FakeContext();
            var a = new CommandA { Context = ctx, Log = log, Label = "A", OnLogic = c => c.Counter++ };
            var b = new CommandB { Log = log, Label = "B" };

            EventOf<CommandA>().AfterCommands += (context, session) => { b.Context = context; return new ICommand[] { b }; };

            var (valid, clone) = RunPreview(a, PreviewAfterMode.None);

            Assert.IsTrue(valid);
            Assert.AreNotSame(ctx, clone);
            Assert.IsTrue(clone.IsPreview);
            CollectionAssert.AreEqual(new[] { "A" }, log, "after-command는 None에서 수집/실행되지 않는다.");

            var previewSession = ((ICommandSessionCarrier)clone).CommandSession;
            Assert.AreEqual(0, previewSession.QueuedCommandCount, "None은 CollectAfterCommands를 호출하지 않는다.");
            Assert.AreEqual(0, ctx.Counter, "원본 Context는 불변이어야 한다.");
            Assert.AreEqual(1, clone.Counter, "Logic은 clone 위에서 적용된다.");
        }

        [Test]
        public void Preview_CollectCommands_QueuesButDoesNotExecute()
        {
            var log = new List<string>();
            var ctx = new FakeContext();
            var a = new CommandA { Context = ctx, Log = log, Label = "A" };
            var b = new CommandB { Log = log, Label = "B" };

            EventOf<CommandA>().AfterCommands += (context, session) => { b.Context = context; return new ICommand[] { b }; };

            var (valid, clone) = RunPreview(a, PreviewAfterMode.CollectCommands);

            Assert.IsTrue(valid);
            CollectionAssert.AreEqual(new[] { "A" }, log, "CollectCommands는 큐잉만 하고 실행하지 않는다.");

            var previewSession = ((ICommandSessionCarrier)clone).CommandSession;
            Assert.Greater(previewSession.QueuedCommandCount, 0, "after-command가 큐에 들어가야 한다.");
        }

        [Test]
        public void Preview_SimulateCommands_RunsOnCloneAndLeavesOriginalUntouched()
        {
            var log = new List<string>();
            var ctx = new FakeContext();
            var a = new CommandA { Context = ctx, Log = log, Label = "A", OnLogic = c => c.Counter++ };
            var b = new CommandB { Log = log, Label = "B", OnLogic = c => c.Counter++ };

            EventOf<CommandA>().AfterCommands += (context, session) => { b.Context = context; return new ICommand[] { b }; };

            var (valid, clone) = RunPreview(a, PreviewAfterMode.SimulateCommands);

            Assert.IsTrue(valid);
            CollectionAssert.AreEqual(new[] { "A", "B" }, log, "SimulateCommands는 clone 그래프 위에서 끝까지 실행한다.");
            Assert.AreEqual(0, ctx.Counter, "원본 Context는 불변이어야 한다.");
            Assert.AreEqual(2, clone.Counter, "A와 B 모두 clone 위에서 Counter를 증가시킨다.");

            var previewSession = ((ICommandSessionCarrier)clone).CommandSession;
            Assert.AreEqual(0, previewSession.QueuedCommandCount, "시뮬레이션 후 큐는 비어 있어야 한다.");
        }
    }
}
