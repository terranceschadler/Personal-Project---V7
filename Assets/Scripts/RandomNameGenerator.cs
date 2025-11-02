using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RandomNameGenerator : MonoBehaviour {

    // List of Names and a name maker 

    public List<string> womensFirstNames;
    public List<string> mensFirstNames;
    public List<string> surNames;

    // Use this for initialization
    void Start()
    {
        FirstNameList(true);
        FirstNameList(false);
        SurNameList();
    }

    void FirstNameList(bool male)
    {
        if (male)
        {
            TextAsset firstNameText = Resources.Load<TextAsset>("FirstNames_Men");
            string[] lines = firstNameText.text.Split("\n"[0]);

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] != "")
                {
                    mensFirstNames.Add(lines[i]);
                }
            }
        }
        if (!male)  //female
        {
            TextAsset firstNameText = Resources.Load<TextAsset>("FirstNames_Women");
            string[] lines = firstNameText.text.Split("\n"[0]);

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] != "")
                {
                    womensFirstNames.Add(lines[i]);
                }
            }
        }


    }

    void SurNameList()
    {
        TextAsset surNameText = Resources.Load<TextAsset>("SurNames");

        string[] lines = surNameText.text.Split("\n"[0]);

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i] != "")
            {
                surNames.Add(lines[i]);
            }
        }
    }


    public string GenerateName()
    {
        int rndGender = Random.Range(0, 1);
        string firstname;
        string surname;
        string returnName;
        if (rndGender == 0) // female
        {
            firstname = womensFirstNames[Random.Range(0, womensFirstNames.Count)];
            surname = surNames[Random.Range(0, surNames.Count)];
            returnName = firstname + " " + surname;
            return returnName;
        }
        if (rndGender == 0) // male
        {
            firstname = mensFirstNames[Random.Range(0, mensFirstNames.Count)];
            surname = surNames[Random.Range(0, surNames.Count)];
            returnName = firstname + " " + surname;
            return returnName;
        }
        return "ERROR";


    }

    public string GenerateZedName(string humanName)
    {
        string firstname = humanName;
        string surname = "ZOMBIE";

        if (firstname == "DEFAULT")
        {
            int rndGender = Random.Range(0, 1);
            if (rndGender == 0)
            {
                firstname = mensFirstNames[Random.Range(0, mensFirstNames.Count)];
            }
            if (rndGender ==1)
            {
                firstname = womensFirstNames[Random.Range(0, womensFirstNames.Count)];
            }

            
        }
        string returnName = firstname + " " + surname;
        return returnName;



    }

    public void PrintCharacterName()
    {
        print(GenerateName());
    }


}
