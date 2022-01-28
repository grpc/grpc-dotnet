#region Copyright notice and license

// Copyright 2019 The gRPC Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#endregion

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Grpc.Core;
using Grpc.Net.Client.Balancer;

namespace Frontend.Balancer
{
    public class SubchannelReporter : IObservable<SubchannelReporterResult>
    {
        private readonly ObservableCollection<IObserver<SubchannelReporterResult>> _observers;
        private SubchannelReporterResult _lastResult = new SubchannelReporterResult(ConnectivityState.Idle, new List<ReportedSubchannelState>());

        public SubchannelReporter()
        {
            _observers = new ObservableCollection<IObserver<SubchannelReporterResult>>();
            _observers.CollectionChanged += OnObserversChanged;
        }

        private void OnObserversChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            NotifySubscriptersCore();
        }

        public void NotifySubscribers(SubchannelReporterResult result)
        {
            _lastResult = result;

            NotifySubscriptersCore();
        }

        private void NotifySubscriptersCore()
        {
            foreach (var observer in _observers)
            {
                NotifySubscriber(observer, _lastResult);
            }
        }

        private void NotifySubscriber(IObserver<SubchannelReporterResult> observer, SubchannelReporterResult result)
        {
            observer.OnNext(result);
        }

        public IDisposable Subscribe(IObserver<SubchannelReporterResult> observer)
        {
            _observers.Add(observer);
            return new Subscription(this, observer);
        }

        private void Unsubscribe(IObserver<SubchannelReporterResult> observer)
        {
            _observers.Remove(observer);
        }

        private class Subscription : IDisposable
        {
            private readonly SubchannelReporter _reporter;
            private readonly IObserver<SubchannelReporterResult> _observer;

            public Subscription(SubchannelReporter reporter, IObserver<SubchannelReporterResult> observer)
            {
                _reporter = reporter;
                _observer = observer;
            }

            public void Dispose()
            {
                _reporter.Unsubscribe(_observer);
            }
        }
    }

    public record SubchannelReporterResult(ConnectivityState State, List<ReportedSubchannelState> Subchannels);
}
