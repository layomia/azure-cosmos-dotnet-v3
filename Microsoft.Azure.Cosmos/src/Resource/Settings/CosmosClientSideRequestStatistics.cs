﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using Microsoft.Azure.Documents;
    using Newtonsoft.Json;

    internal sealed class CosmosClientSideRequestStatistics : IClientSideRequestStatistics
    {
        private static JsonSerializerSettings SerializerSettings = new JsonSerializerSettings()
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None
        };
        internal const int MaxSupplementalRequestsForToString = 10;

        internal DateTime requestStartTime;
        internal DateTime? requestEndTime;

        private object lockObject = new object();

        public List<StoreResponseStatistics> responseStatisticsList { get; private set; }
        public List<StoreResponseStatistics> supplementalResponseStatisticsList { get; internal set; }
        public Dictionary<string, AddressResolutionStatistics> addressResolutionStatistics { get; private set; }

        public CosmosClientSideRequestStatistics()
        {
            this.requestStartTime = DateTime.UtcNow;
            this.requestEndTime = null;
            this.responseStatisticsList = new List<StoreResponseStatistics>();
            this.supplementalResponseStatisticsList = new List<StoreResponseStatistics>();
            this.addressResolutionStatistics = new Dictionary<string, AddressResolutionStatistics>();
            this.ContactedReplicas = new List<Uri>();
            this.FailedReplicas = new HashSet<Uri>();
            this.RegionsContacted = new HashSet<Uri>();
        }

        public List<Uri> ContactedReplicas { get; set; }

        public HashSet<Uri> FailedReplicas { get; private set; }

        public HashSet<Uri> RegionsContacted { get; private set; }

        public TimeSpan RequestLatency
        {
            get
            {
                if (this.requestEndTime.HasValue)
                {
                    return this.requestEndTime.Value - this.requestStartTime;
                }

                return TimeSpan.MaxValue;
            }
        }

        public bool IsCpuOverloaded
        {
            get
            {
                foreach (StoreResponseStatistics responseStatistics in this.responseStatisticsList)
                {
                    if (responseStatistics.StoreResult.IsClientCpuOverloaded)
                    {
                        return true;
                    }
                }
                foreach (StoreResponseStatistics responseStatistics in this.supplementalResponseStatisticsList)
                {
                    if (responseStatistics.StoreResult.IsClientCpuOverloaded)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public void RecordResponse(DocumentServiceRequest request, StoreResult storeResult)
        {
            DateTime responseTime = DateTime.UtcNow;

            StoreResponseStatistics responseStatistics;
            responseStatistics.RequestResponseTime = responseTime;
            responseStatistics.StoreResult = storeResult;
            responseStatistics.RequestOperationType = request.OperationType;
            responseStatistics.RequestResourceType = request.ResourceType;

            Uri locationEndpoint = request.RequestContext.LocationEndpointToRoute;

            lock (this.lockObject)
            {
                if (!this.requestEndTime.HasValue || responseTime > this.requestEndTime)
                {
                    this.requestEndTime = responseTime;
                }

                if (locationEndpoint != null)
                {
                    this.RegionsContacted.Add(locationEndpoint);
                }

                if (responseStatistics.RequestOperationType == OperationType.Head || responseStatistics.RequestOperationType == OperationType.HeadFeed)
                {
                    this.supplementalResponseStatisticsList.Add(responseStatistics);
                }
                else
                {
                    this.responseStatisticsList.Add(responseStatistics);
                }
            }
        }

        public string RecordAddressResolutionStart(Uri targetEndpoint)
        {
            string identifier = Guid.NewGuid().ToString();
            AddressResolutionStatistics resolutionStats = new AddressResolutionStatistics
            {
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.MaxValue,
                TargetEndpoint = targetEndpoint == null ? "<NULL>" : targetEndpoint.ToString()
            };

            lock (this.lockObject)
            {
                this.addressResolutionStatistics.Add(identifier, resolutionStats);
            }

            return identifier;
        }

        public void RecordAddressResolutionEnd(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                return;
            }

            DateTime responseTime = DateTime.UtcNow;
            lock (this.lockObject)
            {
                if (!this.addressResolutionStatistics.ContainsKey(identifier))
                {
                    throw new ArgumentException("Identifier {0} does not exist. Please call start before calling end.", identifier);
                }

                if (!this.requestEndTime.HasValue || responseTime > this.requestEndTime)
                {
                    this.requestEndTime = responseTime;
                }

                this.addressResolutionStatistics[identifier].EndTime = responseTime;
            }
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            this.AppendToBuilder(sb);
            return sb.ToString();
        }

        public void AppendToBuilder(StringBuilder stringBuilder)
        {
            if (stringBuilder == null)
            {
                throw new ArgumentNullException(nameof(stringBuilder));
            }

            //need to lock in case of concurrent operations. this should be extremely rare since ToString()
            //should only be called at the end of request.
            lock (this.lockObject)
            {
                stringBuilder.AppendLine();

                //first trace request start time, as well as total non-head/headfeed requests made.
                string endTime = this.requestEndTime.HasValue ? this.requestEndTime.Value.ToString("o", CultureInfo.InvariantCulture) : "Not set";
                stringBuilder.AppendFormat(
                   CultureInfo.InvariantCulture,
                   "RequestStartTime: {0}, RequestEndTime: {1},  Number of regions attempted:{2}",
                   this.requestStartTime.ToString("o", CultureInfo.InvariantCulture),
                   endTime,
                   this.RegionsContacted.Count == 0 ? 1 : this.RegionsContacted.Count);
                stringBuilder.AppendLine();

                //take all responses here - this should be limited in number and each one contains relevant information.
                foreach (StoreResponseStatistics item in this.responseStatisticsList)
                {
                    item.AppendToBuilder(stringBuilder);
                    stringBuilder.AppendLine();
                }

                //take all responses here - this should be limited in number and each one is important.
                foreach (AddressResolutionStatistics item in this.addressResolutionStatistics.Values)
                {
                    item.AppendToBuilder(stringBuilder);
                    stringBuilder.AppendLine();
                }

                //only take last 10 responses from this list - this has potential of having large number of entries. 
                //since this is for establishing consistency, we can make do with the last responses to paint a meaningful picture.
                int supplementalResponseStatisticsListCount = this.supplementalResponseStatisticsList.Count;
                int initialIndex = Math.Max(supplementalResponseStatisticsListCount - CosmosClientSideRequestStatistics.MaxSupplementalRequestsForToString, 0);

                if (initialIndex != 0)
                {
                    stringBuilder.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "  -- Displaying only the last {0} head/headfeed requests. Total head/headfeed requests: {1}",
                        CosmosClientSideRequestStatistics.MaxSupplementalRequestsForToString,
                        supplementalResponseStatisticsListCount);
                    stringBuilder.AppendLine();
                }

                for (int i = initialIndex; i < supplementalResponseStatisticsListCount; i++)
                {
                    this.supplementalResponseStatisticsList[i].AppendToBuilder(stringBuilder);
                    stringBuilder.AppendLine();
                }
            }
        }

        internal struct StoreResponseStatistics
        {
            public DateTime RequestResponseTime;
            public StoreResult StoreResult;
            public ResourceType RequestResourceType;
            public OperationType RequestOperationType;

            public override string ToString()
            {
                StringBuilder stringBuilder = new StringBuilder();
                this.AppendToBuilder(stringBuilder);
                return stringBuilder.ToString();
            }

            public void AppendToBuilder(StringBuilder stringBuilder)
            {
                if (stringBuilder == null)
                {
                    throw new ArgumentNullException(nameof(stringBuilder));
                }

                stringBuilder.Append($"ResponseTime: {this.RequestResponseTime.ToString("o", CultureInfo.InvariantCulture)}, ");

                stringBuilder.Append("StoreResult: ");
                if (this.StoreResult != null)
                {
                    this.StoreResult.AppendToBuilder(stringBuilder);
                }

                stringBuilder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    ", ResourceType: {0}, OperationType: {1}",
                    this.RequestResourceType,
                    this.RequestOperationType);
            }
        }

        internal class AddressResolutionStatistics
        {
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public string TargetEndpoint { get; set; }

            public override string ToString()
            {
                StringBuilder stringBuilder = new StringBuilder();
                this.AppendToBuilder(stringBuilder);
                return stringBuilder.ToString();
            }

            public void AppendToBuilder(StringBuilder stringBuilder)
            {
                if (stringBuilder == null)
                {
                    throw new ArgumentNullException(nameof(stringBuilder));
                }

                stringBuilder
                    .Append($"AddressResolution - StartTime: {this.StartTime.ToString("o", CultureInfo.InvariantCulture)}, ")
                    .Append($"EndTime: {this.EndTime.ToString("o", CultureInfo.InvariantCulture)}, ")
                    .Append("TargetEndpoint: ")
                    .Append(this.TargetEndpoint);

            }
        }
    }
}
