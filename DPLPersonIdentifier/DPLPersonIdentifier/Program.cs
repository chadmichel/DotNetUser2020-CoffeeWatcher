using System;
using System.Collections.Generic;
using System.IO;
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
        
        static string TrainingSet => Environment.GetEnvironmentVariable("TRAININGSET");
        static string TestingSet => Environment.GetEnvironmentVariable("TESTINGSET");

        private static string PersonGroupId { get; set; }
        private static FaceClient Client { get; set; }
        
        private static readonly Dictionary<Guid, string> PersonLookup = new Dictionary<Guid, string>();

        static async Task Main(string[] args)
        {
            
            
            Client = new FaceClient(new ApiKeyServiceClientCredentials(SubKey))
            {
                Endpoint = Endpoint,
            };

            await Train();
            await Test();
            
            Console.WriteLine("TEST");
        }

        static async Task Train()
        {
            Console.WriteLine("TRAINING");
            
            const string personGroupName = "dpl";
            var allGroups = await Client.PersonGroup.ListAsync();
            if (allGroups.Count == 0)
            {
                await Client.PersonGroup.CreateAsync(personGroupName, personGroupName);
                allGroups = await Client.PersonGroup.ListAsync();
            }
            // PersonGroup personGroup = null;
            PersonGroupId = allGroups[0].PersonGroupId;
            // personGroup = await Client.PersonGroup.GetAsync(PersonGroupId.ToString());
            
            // delete any existing people
            var list = await Client.PersonGroupPerson.ListAsync(PersonGroupId);
            foreach (var person in list)
            {
                await Client.PersonGroupPerson.DeleteAsync(PersonGroupId, person.PersonId);    
            }
            
            // Make sure we have a training set
            var trainingSetDirectory = new DirectoryInfo(TrainingSet);
            if (!trainingSetDirectory.Exists)
            {
                throw new InvalidOperationException("TRAINING SET DIRECTORY DOES NOT EXIST AT " +
                                                    trainingSetDirectory.FullName);
            }

            // Add people. Each directory is a person
            foreach (var personDir in trainingSetDirectory.GetDirectories())
            {
                // Create Person
                var person = await Client.PersonGroupPerson.CreateAsync(
                    PersonGroupId.ToString(), 
                    personDir.Name, personDir.Name);
                
                Console.WriteLine("person: " + personDir.Name);
                
                PersonLookup.Add(person.PersonId, personDir.Name);
                
                // Add images of that person
                foreach (var personImage in personDir.GetFiles()
                    .Where(f => f.Extension == ".jpeg" || f.Extension == ".png"))
                {
                    Console.WriteLine("image: " + personImage.Name);
                    await using var stream = personImage.OpenRead();
                    await Client.PersonGroupPerson.AddFaceFromStreamAsync(PersonGroupId, person.PersonId, stream);
                }
            }
            
            await Client.PersonGroup.TrainAsync(PersonGroupId);
            while(true)
            {
                var trainingStatus = await Client.PersonGroup.GetTrainingStatusAsync(PersonGroupId);
                if (trainingStatus.Status != TrainingStatusType.Running)
                {
                    break;
                }
                await Task.Delay(5000); // wait 5 seconds
            }
            
            Console.WriteLine("TRAINING DONE");
        }

        static async Task Test()
        {
            Console.WriteLine("TESTING");
            
            // Make sure we have a test set
            var testingSetDirectory = new DirectoryInfo(TestingSet);
            if (!testingSetDirectory.Exists)
            {
                throw new InvalidOperationException("TESTING SET DIRECTORY DOES NOT EXIST AT " +
                                                    testingSetDirectory.FullName);
            }
            
            // Add people. Each directory is a person
            foreach (var personDir in testingSetDirectory.GetDirectories())
            {
                Console.WriteLine("person: " + personDir.Name);
                
                // Test images of that person
                foreach (var testPersonImage in 
                    personDir.GetFiles().Where(f => f.Extension == ".jpeg" || f.Extension == ".png"))
                {
                    Console.WriteLine("image: " + testPersonImage.Name);
                    await using var stream = testPersonImage.OpenRead();
                    var detectedFaces = await Client.Face.DetectWithStreamAsync(stream);
                    var found = await Client.Face.IdentifyAsync(
                        detectedFaces.Select(df => df.FaceId).ToList(), PersonGroupId);
                    foreach (var foundPerson in found)
                    {
                        var foundPersonName = PersonLookup[foundPerson.Candidates.First().PersonId];
                        Console.WriteLine(foundPersonName);

                        if (foundPersonName != personDir.Name)
                        {
                            throw new InvalidOperationException($"{foundPersonName} does not match {personDir.Name} for image {testPersonImage.Name}");
                        }
                    }
                }
            }
            
            Console.WriteLine("TESTING DONE");
        }
        
        static async Task FindDoug()
        {

            // 2. Create a Person Group
            const string personGroupName = "dpl";
            var allGroups = await Client.PersonGroup.ListAsync();
            if (allGroups.Count == 0)
            {
                await Client.PersonGroup.CreateAsync(personGroupName, personGroupName);
                allGroups = await Client.PersonGroup.ListAsync();
            }
            PersonGroup personGroup = null;
            var personGroupId = allGroups[0].PersonGroupId;
            personGroup = await Client.PersonGroup.GetAsync(personGroupId.ToString());

            // 3. Create Person - aka Create Doug
            // var list = await client.PersonGroupPerson.ListAsync(personGroupId);
            // foreach (var person in list)
            // {
            //     // delete any extra Doug's lying around.
            //     await client.PersonGroupPerson.DeleteAsync(personGroupId, person.PersonId);    
            // }
            var doug = await Client.PersonGroupPerson.CreateAsync(personGroupId.ToString(), "Doug", "Doug Durham");
            
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
                await Client.PersonGroupPerson.AddFaceFromUrlAsync(personGroup.PersonGroupId.ToString(), doug.PersonId, urlOfDoug);
            }
        
            // 5. Train the image recognizer 
            await Client.PersonGroup.TrainAsync(personGroupId);
            while(true)
            {
                var trainingStatus = await Client.PersonGroup.GetTrainingStatusAsync(personGroupId);
                if (trainingStatus.Status != TrainingStatusType.Running)
                {
                    break;
                }
                await Task.Delay(5000); // wait 5 seconds
            }
            
            // 6. Now test an image of Doug to see if it is an image of Doug
            var urlOfMaybeDoug =
                "https://chadtest2storage.blob.core.windows.net/faces/6bf9547d-1b07-46a1-91de-3726449cd1b5.jpg";
            var detectedFaces = new List<DetectedFace>((await Client.Face.DetectWithUrlAsync(urlOfMaybeDoug)).ToArray());
            var found = await Client.Face.IdentifyAsync(detectedFaces.Select(df => df.FaceId).ToList(), personGroupId);
            Console.WriteLine("Found face " + found.Count);
            Console.WriteLine("Found person = " + found.First().Candidates.First().PersonId);
            Console.WriteLine("Doug person = " + doug.PersonId);
        }
    }
}