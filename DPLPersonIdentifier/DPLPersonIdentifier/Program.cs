using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.CognitiveServices.Vision.Face;
using Microsoft.Azure.CognitiveServices.Vision.Face.Models;

namespace DPLPersonIdentifier
{
    class Program
    {
        static string SubKey => Environment.GetEnvironmentVariable("AZUREVISION_SUBKEY");
        static string Endpoint => Environment.GetEnvironmentVariable("AZUREVISION_ENDPOINT");
        
        static async Task Main(string[] args)
        {
            Console.WriteLine("TRAIN");

            await FindDoug();
            
            Console.WriteLine("TEST");
        }
        
        static async Task FindDoug()
        {
            // 1. Create a client
            var client = new FaceClient(new ApiKeyServiceClientCredentials(SubKey))
            {
                Endpoint = Endpoint,
            };

            var envVars = Environment.GetEnvironmentVariables();
            
            // 2. Create a Person Group
            const string personGroupName = "dpl";
            var allGroups = await client.PersonGroup.ListAsync();
            if (allGroups.Count == 0)
            {
                await client.PersonGroup.CreateAsync(personGroupName, personGroupName);
                allGroups = await client.PersonGroup.ListAsync();
            }
            PersonGroup personGroup = null;
            var personGroupId = allGroups[0].PersonGroupId;
            personGroup = await client.PersonGroup.GetAsync(personGroupId.ToString());

            // 3. Create Person - aka Create Doug
            // var list = await client.PersonGroupPerson.ListAsync(personGroupId);
            // foreach (var person in list)
            // {
            //     // delete any extra Doug's lying around.
            //     await client.PersonGroupPerson.DeleteAsync(personGroupId, person.PersonId);    
            // }
            var doug = await client.PersonGroupPerson.CreateAsync(personGroupId.ToString(), "Doug", "Doug Durham");
            
            // 4. Add images of Doug to the person Doug
            var urlsOfDoug = new string[]
            {
                "https://chadtest2storage.blob.core.windows.net/faces/384fced5-475e-4956-9f59-658fb57605eb-1.jpg",
                "https://chadtest2storage.blob.core.windows.net/faces/384fced5-475e-4956-9f59-658fb57605eb.jpg",
                "https://chadtest2storage.blob.core.windows.net/faces/675037ec-7904-4192-99f6-2150754b04ac-1.jpg",
                "https://chadtest2storage.blob.core.windows.net/faces/675037ec-7904-4192-99f6-2150754b04ac.jpg"
            };
            foreach (var urlOfDoug in urlsOfDoug)
            {
                await client.PersonGroupPerson.AddFaceFromUrlAsync(personGroup.PersonGroupId.ToString(), doug.PersonId, urlOfDoug);
            }
        
            // 5. Train the image recognizer 
            await client.PersonGroup.TrainAsync(personGroupId);
            while(true)
            {
                var trainingStatus = await client.PersonGroup.GetTrainingStatusAsync(personGroupId);
                if (trainingStatus.Status != TrainingStatusType.Running)
                {
                    break;
                }
                await Task.Delay(5000); // wait 5 seconds
            }
            
            // 6. Now test an image of Doug to see if it is an image of Doug
            var urlOfMaybeDoug =
                "https://chadtest2storage.blob.core.windows.net/faces/6bf9547d-1b07-46a1-91de-3726449cd1b5.jpg";
            var detectedFaces = new List<DetectedFace>((await client.Face.DetectWithUrlAsync(urlOfMaybeDoug)).ToArray());
            var found = await client.Face.IdentifyAsync(detectedFaces.Select(df => df.FaceId).ToList(), personGroupId);
            Console.WriteLine("Found face " + found.Count);
            Console.WriteLine("Found person = " + found.First().Candidates.First().PersonId);
            Console.WriteLine("Doug person = " + doug.PersonId);
        }
    }
}