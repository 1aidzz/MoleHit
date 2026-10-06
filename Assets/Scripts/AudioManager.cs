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

    // 设置面板调完音量后会持久化，下次启动自动恢复
    private const string BgmVolumeKey = "MoleHit.BgmVolume";
    private const string SfxVolumeKey = "MoleHit.SfxVolume";

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

        // 读取上次保存的音量；没有记录时沿用 Inspector 里的默认值
        bgmVolume = PlayerPrefs.GetFloat(BgmVolumeKey, bgmVolume);
        sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, sfxVolume);

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
        // 音量由 sfxSource.volume 统一控制。这里若再乘一次 sfxVolume，
        // 实际音量会变成 sfxVolume 的平方，滑条调起来手感会很奇怪。
        sfxSource.PlayOneShot(hitSound, 1f);
    }

    public void PlayButtonSound()
    {
        if (buttonSound == null) return;
        sfxSource.PlayOneShot(buttonSound, 1f);
    }
    #endregion

    #region 音量控制方法（供设置面板的滑条调用）
    public float BgmVolume
    {
        get { return bgmVolume; }
    }

    public float SfxVolume
    {
        get { return sfxVolume; }
    }

    public void SetBgmVolume(float volume)
    {
        bgmVolume = Mathf.Clamp01(volume);
        if (bgmSource != null) bgmSource.volume = bgmVolume;
        PlayerPrefs.SetFloat(BgmVolumeKey, bgmVolume);
    }

    public void SetSfxVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
        if (sfxSource != null) sfxSource.volume = sfxVolume;
        PlayerPrefs.SetFloat(SfxVolumeKey, sfxVolume);
    }
    #endregion

    private void OnValidate()
    {
        if (bgmSource != null) bgmSource.volume = bgmVolume;
        if (sfxSource != null) sfxSource.volume = sfxVolume;
    }
}
