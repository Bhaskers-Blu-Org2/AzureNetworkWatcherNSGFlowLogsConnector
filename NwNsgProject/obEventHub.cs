﻿using Microsoft.Azure.EventHubs;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Text;
using System.Threading.Tasks;

namespace nsgFunc
{
    public partial class Util
    {
        const int MAXTRANSMISSIONSIZE = 255 * 1024;
//        const int MAXTRANSMISSIONSIZE = 2 * 1024;

        public static async Task obEventHub(string newClientContent, ILogger log)
        {
            string EventHubConnectionString = GetEnvironmentVariable("eventHubConnection");
            string EventHubName = GetEnvironmentVariable("eventHubName");
            if (EventHubConnectionString.Length == 0 || EventHubName.Length == 0)
            {
                log.LogError("Values for eventHubConnection and eventHubName are required.");
                return;
            }

            var connectionStringBuilder = new EventHubsConnectionStringBuilder(EventHubConnectionString)
            {
                EntityPath = EventHubName
            };
            var eventHubClient = EventHubClient.CreateFromConnectionString(connectionStringBuilder.ToString());

            foreach (var bundleOfMessages in bundleMessages(newClientContent, log))
            {
                //log.Info(String.Format("-----Outgoing message is: {0}", bundleOfMessages));

                await eventHubClient.SendAsync(new EventData(Encoding.UTF8.GetBytes(bundleOfMessages)));
            }
        }

        static System.Collections.Generic.IEnumerable<string> bundleMessages(string newClientContent, ILogger log)
        {
            var transmission = new StringBuilder(MAXTRANSMISSIONSIZE);
            transmission.Append("{\"records\":[");
            bool firstRecord = true;
            foreach (var message in denormalizeRecords(newClientContent, null, log))
            {
                //
                // message looks like this:
                //
                // {
                //   "time": "xxx",
                //   "category": "xxx",
                //   "operationName": "xxx",
                //   "version": "xxx",
                //   "deviceExtId": "xxx",
                //   "flowOrder": "xxx",
                //   "nsgRuleName": "xxx",
                //   "dmac|smac": "xxx",
                //   "rt": "xxx",
                //   "src": "xxx",
                //   "dst": "xxx",
                //   "spt": "xxx",
                //   "dpt": "xxx",
                //   "proto": "xxx",
                //   "deviceDirection": "xxx",
                //   "act": "xxx"
                //  }

                if (transmission.Length + message.Length > MAXTRANSMISSIONSIZE)
                {
                    transmission.Append("]}");
                    yield return transmission.ToString();
                    transmission.Clear();
                    transmission.Append("{\"records\":[");
                    firstRecord = true;
                }

                // add comma after existing transmission if it's not the first record
                if (firstRecord)
                {
                    firstRecord = false;
                }
                else
                {
                    transmission.Append(",");
                }

                transmission.Append(message);
            }
            if (transmission.Length > 0)
            {
                transmission.Append("]}");
                yield return transmission.ToString();
            }
        }

        static System.Collections.Generic.IEnumerable<string> denormalizeRecords(string newClientContent, Binder errorRecordBinder, ILogger log)
        {
            //
            // newClientContent looks like this:
            //
            // {
            //   "records":[
            //     {...},
            //     {...}
            //     ...
            //   ]
            // }
            //

            NSGFlowLogRecords logs = JsonConvert.DeserializeObject<NSGFlowLogRecords>(newClientContent);

            string logIncomingJSON = Util.GetEnvironmentVariable("logIncomingJSON");
            Boolean flag;
            if (Boolean.TryParse(logIncomingJSON, out flag))
            {
                if (flag)
                {
                    logErrorRecord(newClientContent, errorRecordBinder, log).Wait();
                }
            }

            foreach (var record in logs.records)
            {
                float version = record.properties.Version;

                foreach (var outerFlow in record.properties.flows)
                {
                    foreach (var innerFlow in outerFlow.flows)
                    {
                        foreach (var flowTuple in innerFlow.flowTuples)
                        {
                            var tuple = new NSGFlowLogTuple(flowTuple, version);

                            var denormalizedObject = new ObjectDenormalizer(
                                record.properties.Version,
                                record.time,
                                record.category,
                                record.operationName,
                                record.resourceId,
                                outerFlow.rule,
                                innerFlow.mac,
                                tuple);
                            string outgoingJson = denormalizedObject.ToString();

                            yield return outgoingJson;
                        }
                    }
                }
            }
        }
    }
}
