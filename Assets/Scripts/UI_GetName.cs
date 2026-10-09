using UnityEngine;
using TMPro;
using System.Runtime.InteropServices;

public class UI_GetName : MonoBehaviour     //для получения имени с канваса вешается на инпут
{
#if UNITY_WEBGL


    [DllImport("__Internal")]
    private static extern string getUserNickname();

#else

#endif

    [SerializeField] private TMP_InputField _inputField;

    private string _name;
    private void Start()
    {
        if (PlayerPrefs.HasKey("name"))
        {
            string savedName = PlayerPrefs.GetString("name", "");
            if (!string.IsNullOrWhiteSpace(savedName))
            {
                _inputField.text = savedName;
                _name = savedName;
            }
        }

#if UNITY_WEBGL 
        Debug.Log("This is a WebJL build!");
        Debug.Log("Unity Get Nickname: " + getUserNickname());
        if (getUserNickname() != null && getUserNickname() != "" && getUserNickname() != "false")
        {
            _inputField.text = getUserNickname();
            _name = getUserNickname();
        }
        else if (string.IsNullOrEmpty(_inputField.text))
        {
            _inputField.text = "Player1";
            _name = "Player1";
        }
        Debug.Log(_name);
#else
            Debug.Log("This is not a WebJL build.");
#endif
        SaveName();
    }
    public void SaveName()
    {
        _name = _inputField.text;
        PlayerPrefs.SetString("name", _name);
    }
}
