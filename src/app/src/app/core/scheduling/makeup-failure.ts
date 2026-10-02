import { createFailureMessages } from '../http/failure-messages';
import { MakeupFailure } from './makeup.models';

/**
 * The words for every makeup refusal (S-36). A screen decides where a refusal goes; it never writes
 * the sentence (S-19).
 *
 * Phrased for staff, about the member, like the booking table — and in the makeup's own terms where the
 * booking table's sentence would mislead: "brak wolnych wejść" never applies, because a makeup costs
 * no entry.
 */
const MESSAGES: Record<MakeupFailure['reason'], string> = {
  makeup_not_open: 'Te zajęcia nie czekają już na odrobienie — lista była nieaktualna.',
  makeup_deadline_passed: 'Te zajęcia wypadają po terminie odrobienia.',
  makeup_not_reopenable: 'Tej pozycji nie można już otworzyć — minął termin odrobienia.',
  class_full: 'Na tych zajęciach nie ma już wolnych miejsc.',
  class_started: 'Te zajęcia już się rozpoczęły.',
  class_cancelled: 'Te zajęcia zostały odwołane.',
  already_booked: 'Ta osoba jest już zapisana na te zajęcia.',
  no_valid_pass: 'Ta osoba nie ma karnetu ważnego w dniu tych zajęć.',
  member_blocked: 'Ta osoba jest zablokowana i nie może odrabiać zajęć.',
  member_is_staff: 'Trenerzy i administratorzy nie odrabiają zajęć.',
  conflict: 'Ktoś właśnie zmienił to odrabianie. Spróbuj ponownie.',
};

const UNKNOWN = 'Nie udało się zmienić odrabiania. Spróbuj ponownie za chwilę.';

export const makeupFailureMessage = createFailureMessages(MESSAGES, UNKNOWN);
