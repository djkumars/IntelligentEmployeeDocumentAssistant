import { HttpClient } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { Component, ElementRef, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';

interface Citation {
  sourceName: string;
  sourceUrl: string;
  excerpt: string;
}

interface ChatMessage {
  role: 'assistant' | 'user';
  content: string;
  citations?: Citation[];
  pending?: boolean;
}

interface ChatResponse {
  answer: string;
  citations: Citation[];
}

declare global {
  interface Window {
    employeeDocumentAssistantConfig?: {
      apiBaseUrl: string;
    };
  }
}

@Component({
  selector: 'hr-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.component.html',
  styleUrl: './app.component.css'
})
export class AppComponent {
  @ViewChild('conversation') private conversation?: ElementRef<HTMLElement>;
  private readonly http = inject(HttpClient);

  question = '';
  isLoading = false;
  errorMessage = '';
  selectedCategory = 'All documents';
  readonly categories = ['All documents', 'People & culture', 'Benefits', 'Workplace'];
  readonly suggestions = [
    { icon: '✳', text: 'How many days of parental leave can I take?', category: 'Benefits' },
    { icon: '↗', text: 'What is the policy for booking business travel?', category: 'Workplace' },
    { icon: '♡', text: 'How do I add a dependent to my health insurance?', category: 'Benefits' }
  ];
  messages: ChatMessage[] = [
    {
      role: 'assistant',
      content: 'Hi Alex! I’m your PeopleDesk assistant. Ask me anything about company policies, benefits, or the employee handbook — I’ll find the answer and show you exactly where it came from.'
    }
  ];

  askSuggestion(text: string): void {
    this.question = text;
    this.sendMessage();
  }

  sendMessage(): void {
    const question = this.question.trim();
    if (!question || this.isLoading) return;

    this.messages = [...this.messages, { role: 'user', content: question }];
    this.question = '';
    this.errorMessage = '';
    this.isLoading = true;
    this.messages = [...this.messages, { role: 'assistant', content: '', pending: true }];
    this.scrollToBottom();

    const apiBaseUrl = window.employeeDocumentAssistantConfig?.apiBaseUrl.replace(/\/+$/, '') ?? '';
    this.http.post<ChatResponse>(`${apiBaseUrl}/api/chat`, {
      question,
      category: this.selectedCategory === 'All documents' ? null : this.selectedCategory
    }).subscribe({
      next: (response) => {
        this.messages = [
          ...this.messages.slice(0, -1),
          { role: 'assistant', content: response.answer, citations: response.citations }
        ];
        this.isLoading = false;
        this.scrollToBottom();
      },
      error: (error: unknown) => {
        const detail = this.getErrorDetail(error);
        this.messages = this.messages.slice(0, -1);
        this.errorMessage = detail;
        this.isLoading = false;
        this.scrollToBottom();
      }
    });
  }

  onComposerKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.sendMessage();
    }
  }

  private getErrorDetail(error: unknown): string {
    if (typeof error === 'object' && error !== null && 'error' in error) {
      const body = (error as { error?: { error?: string; detail?: string; title?: string } }).error;
      if (body?.error) return body.error;
      if (body?.detail) return body.detail;
      if (body?.title) return body.title;
    }
    return 'I couldn’t reach the assistant. Please check that the API is running, then try again.';
  }

  private scrollToBottom(): void {
    setTimeout(() => {
      const element = this.conversation?.nativeElement;
      if (element) element.scrollTop = element.scrollHeight;
    });
  }
}
