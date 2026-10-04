using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager instance;

    #region 音频源组件
    private AudioSource bgmSource;   // 背景音乐专用，循环播放
    private AudioSource sfxSource;   // 音效专用，一次性短音频
    #endregion

    #region 音频素材
    [Header("背景音乐 BGM")]
    public AudioClip menuBgm;        // 开始界面菜单BGM
    public AudioClip gameBgm;        // 游戏进行中循环BGM
    public AudioClip winBgm;         // 胜利结算循环BGM
    public AudioClip loseBgm;        // 失败结算循环BGM
    #endregion

    #region 音效 SFX
    [Header("音效 SFX")]
    public AudioClip hitSound;       // 锤子击中地鼠
    public AudioClip buttonSound;    // UI按钮点击
    #endregion

    #region 音量控制
    [Header("音量设置")]
    [Range(0f, 1f)] public float bgmVolume = 0.6f;
    [Range(0f, 1f)] public float sfxVolume = 0.8f;
    #endregion

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.loop = true;
        bgmSource.playOnAwake = false;
        bgmSource.volume = bgmVolume;

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.loop = false;
        sfxSource.playOnAwake = false;
        sfxSource.volume = sfxVolume;
    }

    #region BGM 控制方法
    public void PlayMenuBGM()
    {
        if (menuBgm == null) return;
        bgmSource.clip = menuBgm;
        bgmSource.Play();
    }

    public void PlayGameBGM()
    {
        if (gameBgm == null) return;
        bgmSource.clip = gameBgm;
        bgmSource.Play();
    }

    public void PlayWinBGM()
    {
        if (winBgm == null) return;
        bgmSource.clip = winBgm;
        bgmSource.Play();
    }

    public void PlayLoseBGM()
    {
        if (loseBgm == null) return;
        bgmSource.clip = loseBgm;
        bgmSource.Play();
    }

    public void StopBGM()
    {
        bgmSource.Stop();
    }
    #endregion

    #region 音效控制方法
    public void PlayHitSound()
    {
        if (hitSound == null) return;
        sfxSource.PlayOneShot(hitSound, sfxVolume);
    }

    public void PlayButtonSound()
    {
        if (buttonSound == null) return;
        sfxSource.PlayOneShot(buttonSound, sfxVolume);
    }
    #endregion

    private void OnValidate()
    {
        if (bgmSource != null) bgmSource.volume = bgmVolume;
        if (sfxSource != null) sfxSource.volume = sfxVolume;
    }
}
