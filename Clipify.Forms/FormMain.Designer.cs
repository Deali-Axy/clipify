namespace Clipify.Forms;

partial class FormMain {
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing) {
        if (disposing && (components != null)) {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent() {
        System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormMain));
        blazorWebView1 = new Microsoft.AspNetCore.Components.WebView.WindowsForms.BlazorWebView();
        openFileDialog = new System.Windows.Forms.OpenFileDialog();
        folderBrowserDialog = new System.Windows.Forms.FolderBrowserDialog();
        SuspendLayout();
        // 
        // blazorWebView1
        // 
        blazorWebView1.Dock = System.Windows.Forms.DockStyle.Fill;
        blazorWebView1.Location = new System.Drawing.Point(0, 0);
        blazorWebView1.Margin = new System.Windows.Forms.Padding(6, 5, 6, 5);
        blazorWebView1.Name = "blazorWebView1";
        blazorWebView1.Size = new System.Drawing.Size(2690, 1631);
        blazorWebView1.StartPath = "/";
        blazorWebView1.TabIndex = 0;
        blazorWebView1.Text = "blazorWebView1";
        blazorWebView1.DragDrop += blazorWebView1_DragDrop;
        blazorWebView1.DragEnter += blazorWebView1_DragEnter;
        blazorWebView1.DragOver += blazorWebView1_DragOver;
        // 
        // openFileDialog
        // 
        openFileDialog.RestoreDirectory = true;
        // 
        // FormMain
        // 
        AutoScaleDimensions = new System.Drawing.SizeF(14F, 31F);
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        ClientSize = new System.Drawing.Size(2690, 1631);
        Controls.Add(blazorWebView1);
        Icon = ((System.Drawing.Icon)resources.GetObject("$this.Icon"));
        Margin = new System.Windows.Forms.Padding(6, 5, 6, 5);
        StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        Text = "Clipify - 简单流畅的视频编辑体验";
        ResumeLayout(false);
    }

    #endregion

    private Microsoft.AspNetCore.Components.WebView.WindowsForms.BlazorWebView blazorWebView1;
    public OpenFileDialog openFileDialog;
    public FolderBrowserDialog folderBrowserDialog;
}