using System;
using System.Collections;
using System.IO;
using UnityEngine;

// Screenshot tour of every screen, for design review on a desktop build (Builder.BuildLinuxTour).
// Only runs when the player is started with -uitour <folder>; it never runs on a phone.
public partial class GameUI
{
    void StartTourIfAsked()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-uitour");
        if (i < 0 || i + 1 >= args.Length) return;
        Progress.Sound = false;
        StartCoroutine(Tour(args[i + 1]));
    }

    IEnumerator Shot(string folder, string name)
    {
        yield return new WaitForSecondsRealtime(0.9f); // let tweens settle
        yield return new WaitForEndOfFrame();
        // Read the finished frame (the ScreenCapture module isn't in this project).
        var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        tex.Apply();
        File.WriteAllBytes(Path.Combine(folder, name + ".png"), tex.EncodeToPNG());
        Destroy(tex);
        yield return null;
        Debug.Log("[UiTour] " + name);
    }

    IEnumerator Play(int level)
    {
        yield return LoadLevel(level);
        CloseAllPopups();
    }

    IEnumerator Moves(int count)
    {
        for (int m = 0; m < count && board.State == TangleBoard.GameState.Playing; m++)
        {
            int k = board.NextGoodKite();
            if (k < 0) break;
            board.Tap(k);
            yield return new WaitForSecondsRealtime(0.15f);
        }
    }

    IEnumerator Tour(string folder)
    {
        Directory.CreateDirectory(folder);
        yield return new WaitForSecondsRealtime(1f);
        yield return Shot(folder, "01-home");
        ShowMap();
        yield return Shot(folder, "02-map");
        ShowHome();

        yield return LoadLevel(1); // first time: the How to play card
        yield return Shot(folder, "03-tutorial-card");
        CloseAllPopups();
        yield return Shot(folder, "04-level1");

        yield return Play(5);
        yield return Shot(folder, "05-level5-day");
        yield return Moves(5);
        yield return Shot(folder, "06-level5-moves");
        board.ShowHint();
        yield return new WaitForSecondsRealtime(0.3f);
        yield return Shot(folder, "07-hint");

        yield return Play(8);
        yield return Shot(folder, "08-level8-dusk");
        yield return Play(13);
        yield return Shot(folder, "09-level13-night");

        OpenPopup(pause);
        yield return Shot(folder, "10-pause");
        CloseAllPopups();
        OpenPopup(settings);
        yield return Shot(folder, "11-settings");
        CloseAllPopups();
        OpenPopup(shop);
        yield return Shot(folder, "12-shop");
        CloseAllPopups();

        for (int t = 0; t < 3; t++)
        {
            int k = board.SomeCoveredKite();
            if (k >= 0) board.Tap(k);
        }
        yield return new WaitForSecondsRealtime(1f);
        yield return Shot(folder, "13-lose");

        yield return Play(2);
        yield return Moves(40);
        yield return new WaitForSecondsRealtime(2.5f);
        yield return Shot(folder, "14-win");

        ShowHome();
        ShowStats();
        yield return Shot(folder, "15-stats");

        Debug.Log("[UiTour] done");
        Application.Quit();
    }
}
