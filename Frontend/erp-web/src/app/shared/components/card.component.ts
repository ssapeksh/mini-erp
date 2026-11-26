import { ChangeDetectionStrategy, Component, ContentChild, ElementRef, input } from '@angular/core';

/**
 * CONTENT PROJECTION (like React's children): the parent puts HTML between
 * <app-card> tags and <ng-content> decides where it appears.
 *   <ng-content select="[card-actions]">  -> only elements with the card-actions attribute
 *   <ng-content />                        -> everything else (the body)
 *
 * @ContentChild queries the PROJECTED content (what the parent passed in);
 * @ViewChild queries the component's OWN template. Here we check whether the
 * parent projected an actions slot, so the header layout can adapt.
 */
@Component({
  selector: 'app-card',
  template: `
    <section class="card">
      @if (title() || actions) {
        <header class="card-header">
          @if (title()) { <h2>{{ title() }}</h2> }
          <div class="card-actions"><ng-content select="[card-actions]" /></div>
        </header>
      }
      <ng-content />
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CardComponent {
  readonly title = input<string>('');

  /** Finds <div card-actions #cardActions> passed by the parent (undefined if none). */
  @ContentChild('cardActions', { read: ElementRef }) actions?: ElementRef<HTMLElement>;
}
