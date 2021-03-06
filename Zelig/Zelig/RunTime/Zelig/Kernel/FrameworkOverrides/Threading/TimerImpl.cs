//
// Copyright (c) Microsoft Corporation.    All rights reserved.
//

namespace Microsoft.Zelig.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Runtime.CompilerServices;

    using TS = Microsoft.Zelig.Runtime.TypeSystem;


    [ExtendClass(typeof(System.Threading.Timer))]
    public class TimerImpl : IDisposable
    {
        internal class TimerPool
        {
            internal class CallbackHandler
            {
                //
                // State
                //

                TimerPool               m_owner;
                Thread                  m_thread;
                AutoResetEvent          m_event;
                KernelNode< TimerImpl > m_node;

                //
                // Constructor Methods
                //

                internal CallbackHandler( TimerPool owner )
                {
                    m_owner  = owner;
                    m_thread = new Thread( Worker );
                    m_event  = new AutoResetEvent( false );

                    m_thread.Start();
                }

                //
                // Helper Methods
                //

                internal void Process( KernelNode< TimerImpl > node )
                {
                    m_node = node;
                    m_event.Set();
                }

                internal void Shutdown()
                {
                    m_node = null;
                    m_event.Set();
                }

                private void Worker()
                {
                    while(true)
                    {
                        m_event.WaitOne();

                        KernelNode< TimerImpl > node = m_node;
                        if(node == null)
                        {
                            break;
                        }

                        TimerImpl target = node.Target;

                        node.Target.Execute();

                        m_owner.Done( this, node );
                    }
                }
            }

            //
            // State
            //

            private static readonly int c_MaxIdleHandlers = Configuration.DefaultTimerPoolThreads;
            
            KernelList< TimerImpl >  m_timers;

            Thread                   m_controller;
            EventWaitHandleImpl      m_controllerWakeup;

            Queue< CallbackHandler > m_idleHandlers;
            
            //
            // Constructor Methods
            //

            public TimerPool()
            {
                m_timers           = new KernelList< TimerImpl >();
                m_controllerWakeup = new EventWaitHandleImpl( false, System.Threading.EventResetMode.AutoReset );
                m_controller       = new Thread( ControllerMethod );
                m_idleHandlers     = new Queue< CallbackHandler >();

                m_controller.Start();
            }


            //
            // Helper Methods
            //

            internal void Activate( KernelNode< TimerImpl > node )
            {
                lock(this)
                {
                    TimerImpl timer = node.Target;

                    if(timer.m_fExecuting == false)
                    {
                        node.RemoveFromList();

                        //
                        // Insert in order.
                        //
                        SchedulerTime           nextTrigger       = timer.m_nextTrigger;
                        KernelNode< TimerImpl > node2             = m_timers.StartOfForwardWalk;
                        bool                    fSignalController = true;

                        while(node2.IsValidForForwardMove)
                        {
                            if(node2.Target.m_nextTrigger > nextTrigger)
                            {
                                break;
                            }

                            node2             = node2.Next;
                            fSignalController = false;
                        }

                        node.InsertBefore( node2 );

                        if(fSignalController)
                        {
                            m_controllerWakeup.Set();
                        }
                    }
                }
            }

            internal void Deactivate( KernelNode< TimerImpl > node )
            {
                lock(this)
                {
                    node.Target.m_nextTrigger = SchedulerTime.MaxValue;

                    //
                    // No need to wake up the controller, even if this is the first timer on the list.
                    // At most the controller will wake up early for the next timer, not late.
                    //
                    node.RemoveFromList();
                }
            }

            internal void Done( CallbackHandler         worker ,
                                KernelNode< TimerImpl > node   )
            {
                lock(this)
                {
                    if(m_idleHandlers.Count < c_MaxIdleHandlers)
                    {
                        m_idleHandlers.Enqueue( worker );
                        worker = null;
                    }

                    //--//

                    TimerImpl timer = node.Target;

                    if(timer.IsDisposed == false)
                    {
                        if(timer.m_nextTrigger != SchedulerTime.MaxValue)
                        {
                            Activate( node );
                        }
                    }
                }

                if(worker != null)
                {
                    worker.Shutdown();
                }
            }

            //--//

            private void ControllerMethod()
            {
                while(true)
                {
                    SchedulerTime waitFor = SchedulerTime.MaxValue;
                    SchedulerTime now     = SchedulerTime.Now;

                    while(true)
                    {
                        KernelNode< TimerImpl > node;

                        lock(this)
                        {
                            node = m_timers.FirstNode();
                            if(node != null)
                            {
                                TimerImpl timer = node.Target;

                                if(timer.m_nextTrigger <= now)
                                {
                                    node.RemoveFromList();

                                    timer.PrepareForExecution();
                                }
                                else
                                {
                                    waitFor = timer.m_nextTrigger;

                                    node = null;
                                }
                            }
                        }

                        if(node == null)
                        {
                            break;
                        }

                        CallbackHandler worker = FetchIdleWorker();

                        worker.Process( node );
                    }

                    m_controllerWakeup.WaitOne( waitFor, false );
                }
            }

            private CallbackHandler FetchIdleWorker()
            {
                CallbackHandler worker;

                lock(this)
                {
                    if(m_idleHandlers.Count > 0)
                    {
                        worker = m_idleHandlers.Dequeue();
                    }
                    else
                    {
                        worker = null;
                    }
                }

                if(worker == null)
                {
                    worker = new CallbackHandler( this );
                }

                return worker;
            }

            //
            // Access Methods
            //

        }

        //
        // State
        //

        static TimerPool s_pool;

        TimerCallback           m_callback;
        object                  m_state;
        SchedulerTimeSpan       m_dueTime;
        SchedulerTimeSpan       m_period;

        KernelNode< TimerImpl > m_node;
        SchedulerTime           m_nextTrigger;
        bool                    m_fExecuting;

        //
        // Constructor Methods
        //

        [DiscardTargetImplementation]
        private TimerImpl()
        {
            m_node        = new KernelNode< TimerImpl >( this );
            m_nextTrigger = SchedulerTime.MaxValue;
        }

        [DiscardTargetImplementation]
        public TimerImpl( TimerCallback callback ,
                          Object        state    ,
                          int           dueTime  ,
                          int           period   ) : this()
        {
            m_callback = callback;
            m_state    = state;

            Change( dueTime, period );
        }

        [DiscardTargetImplementation]
        public TimerImpl( TimerCallback callback ,
                          Object        state    ,
                          TimeSpan      dueTime  ,
                          TimeSpan      period   ) : this()
        {
            m_callback = callback;
            m_state    = state;

            Change( dueTime, period );
        }

        [DiscardTargetImplementation]
        public TimerImpl( TimerCallback callback ) : this()
        {
            m_callback = callback;

            Change( Timeout.Infinite, Timeout.Infinite );
        }

        //
        // Helper Methods
        //

        public bool Change( int dueTime ,
                            int period  )
        {
            if(this.IsDisposed)
            {
                return false;
            }

            Deactivate();

            if(period < 0)
            {
                if(period < Timeout.Infinite)
                {
#if EXCEPTION_STRINGS
                    throw new ArgumentOutOfRangeException( "period" );
#else
                    throw new ArgumentOutOfRangeException();
#endif
                }

                m_period = SchedulerTimeSpan.MaxValue;
            }
            else
            {
                m_period = SchedulerTimeSpan.FromMilliseconds( period );
            }

            if(dueTime < 0)
            {
                if(dueTime < Timeout.Infinite)
                {
#if EXCEPTION_STRINGS
                    throw new ArgumentOutOfRangeException( "dueTime" );
#else
                    throw new ArgumentOutOfRangeException();
#endif
                }

                m_dueTime = SchedulerTimeSpan.MaxValue;
            }
            else
            {
                m_dueTime = SchedulerTimeSpan.FromMilliseconds( dueTime );

                SetInitialTrigger();

                Activate();
            }
    
            return true;
        }

        public bool Change( TimeSpan dueTime ,
                            TimeSpan period  )
        {
            if(this.IsDisposed)
            {
                return false;
            }

            Deactivate();

            if(period.Ticks < 0)
            {
                m_period = SchedulerTimeSpan.MaxValue;
            }
            else
            {
                m_period = (SchedulerTimeSpan)period;
            }

            if(dueTime.Ticks < 0)
            {
                m_dueTime = SchedulerTimeSpan.MaxValue;
            }
            else
            {
                m_dueTime = (SchedulerTimeSpan)dueTime;

                SetInitialTrigger();

                Activate();
            }

            return true;
        }

        public bool Change( long dueTime ,
                            long period  )
        {
            if(this.IsDisposed)
            {
                return false;
            }

            Deactivate();

            if(period < 0)
            {
                if(period < Timeout.Infinite)
                {
#if EXCEPTION_STRINGS
                    throw new ArgumentOutOfRangeException( "period" );
#else
                    throw new ArgumentOutOfRangeException();
#endif
                }

                m_period = SchedulerTimeSpan.MaxValue;
            }
            else
            {
                m_period = SchedulerTimeSpan.FromMilliseconds( period );
            }

            if(dueTime < 0)
            {
                if(dueTime < Timeout.Infinite)
                {
#if EXCEPTION_STRINGS
                    throw new ArgumentOutOfRangeException( "dueTime" );
#else
                    throw new ArgumentOutOfRangeException();
#endif
                }

                m_dueTime = SchedulerTimeSpan.MaxValue;
            }
            else
            {
                m_dueTime = SchedulerTimeSpan.FromMilliseconds( dueTime );

                SetInitialTrigger();

                Activate();
            }
    
            return true;
        }

        public void Dispose()
        {
            this.Pool.Deactivate( m_node );

            m_callback = null;
            m_state    = null;
        }

        //--//

        private void SetInitialTrigger()
        {
            m_nextTrigger = SchedulerTime.Now + m_dueTime;
        }

        private void SetNextTrigger()
        {
            if(m_period != SchedulerTimeSpan.MaxValue)
            {
                m_nextTrigger += m_period;
            }
            else
            {
                m_nextTrigger = SchedulerTime.MaxValue;
            }
        }

        private void Activate()
        {
            this.Pool.Activate( m_node );
        }

        private void Deactivate()
        {
            this.Pool.Deactivate( m_node );
        }

        //--//

        void PrepareForExecution()
        {
            m_fExecuting = true;
        }

        void Execute()
        {
            TimerCallback callback;
            object        state;

            lock(this.Pool)
            {
                callback = m_callback;
                state    = m_state;

                if(callback != null)
                {
                    SetNextTrigger();
                }
            }

            try
            {
                if(callback != null)
                {
                    callback( state );
                }
            }
            catch
            {
            }

            m_fExecuting = false;
        }

        //
        // Access Methods
        //

        private bool IsDisposed
        {
            get
            {
                return m_callback == null;
            }
        }

        private TimerPool Pool
        {
            get
            {
                return TypeSystemManager.AtomicAllocator( ref s_pool );
            }
        }
    }
}
