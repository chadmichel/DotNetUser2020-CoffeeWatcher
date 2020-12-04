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
                    var isFound = false;
                    
                    await using var stream = testPersonImage.OpenRead();
                    var detectedFaces = await Client.Face.DetectWithStreamAsync(stream);
                    var found = await Client.Face.IdentifyAsync(
                        detectedFaces.Select(df => df.FaceId).ToList(), PersonGroupId);

                    
                    if (found.Any() && found.First().Candidates.Count > 0)
                    {
                        foreach (var foundPerson in found)
                        {
                            var foundPersonName = PersonLookup[foundPerson.Candidates.First().PersonId];
                            Console.WriteLine(foundPersonName);

                            if (foundPersonName != personDir.Name)
                            {
                                throw new InvalidOperationException(
                                    $"{foundPersonName} does not match {personDir.Name} for image {testPersonImage.Name}");
                            }

                            isFound = true;
                        }
                    }

                    if (isFound && personDir.Name == "noone")
                    {
                        throw new InvalidOperationException(
                            $"no one was matched for image {testPersonImage.Name}");
                    } else if (!isFound && personDir.Name != "noone")
                    {
                        throw new InvalidOperationException($"no person found for  {testPersonImage.Name}");
                    }
                }
            }
            
            Console.WriteLine("TESTING DONE");
        }
    }
}