namespace SourceFilterMail.WinForms;

partial class Form1
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    private Label lblCredential;
    private TextBox txtCredential;
    private Button btnCredential;
    private Label lblOutput;
    private TextBox txtOutput;
    private Button btnOutput;
    private Label lblDate;
    private DateTimePicker dtpDate;
    private Label lblGemini;
    private TextBox txtGemini;
    private Button btnRun;
    private DataGridView dgvResult;
    private TextBox txtLog;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        lblCredential = new Label();
        txtCredential = new TextBox();
        btnCredential = new Button();
        lblOutput = new Label();
        txtOutput = new TextBox();
        btnOutput = new Button();
        lblDate = new Label();
        dtpDate = new DateTimePicker();
        lblGemini = new Label();
        txtGemini = new TextBox();
        btnRun = new Button();
        dgvResult = new DataGridView();
        txtLog = new TextBox();
        ((System.ComponentModel.ISupportInitialize)dgvResult).BeginInit();
        SuspendLayout();
        // 
        // lblCredential
        // 
        lblCredential.AutoSize = true;
        lblCredential.Location = new Point(20, 20);
        lblCredential.Name = "lblCredential";
        lblCredential.Size = new Size(134, 15);
        lblCredential.TabIndex = 0;
        lblCredential.Text = "Credential JSON (Gmail)";
        // 
        // txtCredential
        // 
        txtCredential.Location = new Point(20, 39);
        txtCredential.Name = "txtCredential";
        txtCredential.Size = new Size(700, 23);
        txtCredential.TabIndex = 1;
        // 
        // btnCredential
        // 
        btnCredential.Location = new Point(730, 39);
        btnCredential.Name = "btnCredential";
        btnCredential.Size = new Size(90, 23);
        btnCredential.TabIndex = 2;
        btnCredential.Text = "Choose file";
        btnCredential.UseVisualStyleBackColor = true;
        btnCredential.Click += btnCredential_Click;
        // 
        // lblOutput
        // 
        lblOutput.AutoSize = true;
        lblOutput.Location = new Point(20, 75);
        lblOutput.Name = "lblOutput";
        lblOutput.Size = new Size(85, 15);
        lblOutput.TabIndex = 3;
        lblOutput.Text = "Folder save file";
        // 
        // txtOutput
        // 
        txtOutput.Location = new Point(20, 94);
        txtOutput.Name = "txtOutput";
        txtOutput.Size = new Size(700, 23);
        txtOutput.TabIndex = 4;
        // 
        // btnOutput
        // 
        btnOutput.Location = new Point(730, 94);
        btnOutput.Name = "btnOutput";
        btnOutput.Size = new Size(90, 23);
        btnOutput.TabIndex = 5;
        btnOutput.Text = "Choose folder";
        btnOutput.UseVisualStyleBackColor = true;
        btnOutput.Click += btnOutput_Click;
        // 
        // lblDate
        // 
        lblDate.AutoSize = true;
        lblDate.Location = new Point(20, 130);
        lblDate.Name = "lblDate";
        lblDate.Size = new Size(31, 15);
        lblDate.TabIndex = 6;
        lblDate.Text = "Date";
        // 
        // dtpDate
        // 
        dtpDate.Format = DateTimePickerFormat.Short;
        dtpDate.Location = new Point(20, 149);
        dtpDate.Name = "dtpDate";
        dtpDate.Size = new Size(130, 23);
        dtpDate.TabIndex = 7;
        // 
        // lblGemini
        // 
        lblGemini.AutoSize = true;
        lblGemini.Location = new Point(170, 130);
        lblGemini.Name = "lblGemini";
        lblGemini.Size = new Size(143, 15);
        lblGemini.TabIndex = 8;
        lblGemini.Text = "Gemini API Key (optional)";
        // 
        // txtGemini
        // 
        txtGemini.Location = new Point(170, 149);
        txtGemini.Name = "txtGemini";
        txtGemini.Size = new Size(550, 23);
        txtGemini.TabIndex = 9;
        // 
        // btnRun
        // 
        btnRun.Location = new Point(730, 149);
        btnRun.Name = "btnRun";
        btnRun.Size = new Size(90, 23);
        btnRun.TabIndex = 10;
        btnRun.Text = "Run";
        btnRun.UseVisualStyleBackColor = true;
        btnRun.Click += btnRun_Click;
        // 
        // dgvResult
        // 
        dgvResult.AllowUserToAddRows = false;
        dgvResult.AllowUserToDeleteRows = false;
        dgvResult.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        dgvResult.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        dgvResult.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        dgvResult.Location = new Point(20, 190);
        dgvResult.Name = "dgvResult";
        dgvResult.ReadOnly = true;
        dgvResult.Size = new Size(800, 320);
        dgvResult.TabIndex = 11;
        // 
        // txtLog
        // 
        txtLog.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        txtLog.Location = new Point(20, 520);
        txtLog.Multiline = true;
        txtLog.Name = "txtLog";
        txtLog.ReadOnly = true;
        txtLog.ScrollBars = ScrollBars.Vertical;
        txtLog.Size = new Size(800, 120);
        txtLog.TabIndex = 12;
        // 
        // Form1
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(850, 660);
        Controls.Add(txtLog);
        Controls.Add(dgvResult);
        Controls.Add(btnRun);
        Controls.Add(txtGemini);
        Controls.Add(lblGemini);
        Controls.Add(dtpDate);
        Controls.Add(lblDate);
        Controls.Add(btnOutput);
        Controls.Add(txtOutput);
        Controls.Add(lblOutput);
        Controls.Add(btnCredential);
        Controls.Add(txtCredential);
        Controls.Add(lblCredential);
        Name = "Form1";
        Text = "Source Filter Mail";
        ((System.ComponentModel.ISupportInitialize)dgvResult).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion
}
